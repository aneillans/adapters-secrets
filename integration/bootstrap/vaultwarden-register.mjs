// Registers a fresh account on a self-hosted VaultWarden, seeds its vault, and
// returns a personal API key for it. VaultWarden's vault is end-to-end encrypted,
// so this reproduces the Bitwarden client crypto by hand.
//
// (The official `bw` CLI would be the natural tool for seeding items, but modern
// `bw` refuses any non-HTTPS server URL -- "Insecure URL not allowed" -- which is
// incompatible with the plain-HTTP local stack the .NET adapter reads over. And
// `bw` cannot register an account at all. Since we already hold the user key here,
// creating ciphers directly via POST /api/ciphers is both simpler and uses the
// exact crypto the adapter decrypts.)
//
// The algorithm mirrors Neillans.Adapters.Secrets.BitWarden/BitWardenCrypto.cs:
//   masterKey            = PBKDF2-SHA256(password, lowercased-email, iterations, 32)
//   masterPasswordHash   = base64(PBKDF2-SHA256(masterKey, password, 1, 32))
//   stretched enc/mac    = HKDF-Expand-SHA256(masterKey, "enc"/"mac", 32)
//   type-2 EncString     = "2." b64(iv) "|" b64(ct) "|" b64(HMAC-SHA256(mac, iv‖ct))
//   protected user key   = EncString(random 64B user key, stretched enc/mac)
//   encryptedPrivateKey  = EncString(RSA PKCS8 DER, user key enc/mac)
//   publicKey            = base64(RSA SPKI DER)
//
// Prints a single JSON line { clientId, clientSecret, userId } to stdout; all
// human-readable progress goes to stderr.

import crypto from 'node:crypto';

const {
  VW_URL,
  BITWARDEN_EMAIL: EMAIL,
  BITWARDEN_MASTER_PASSWORD: PASSWORD,
  BITWARDEN_KDF_ITERATIONS,
  SMOKE_SEED_JSON,
} = process.env;

const ITER = parseInt(BITWARDEN_KDF_ITERATIONS || '600000', 10);

for (const [k, v] of Object.entries({ VW_URL, BITWARDEN_EMAIL: EMAIL, BITWARDEN_MASTER_PASSWORD: PASSWORD })) {
  if (!v) { console.error(`Missing required env var ${k}`); process.exit(2); }
}

const log = (...a) => console.error('[vw-register]', ...a);

// ---- crypto helpers ---------------------------------------------------------

const utf8 = (s) => Buffer.from(s, 'utf8');

function pbkdf2(password, salt, iterations, len) {
  return crypto.pbkdf2Sync(password, salt, iterations, len, 'sha256');
}

// HKDF-Expand only (PRK = masterKey), matching HKDF.Expand in the C# adapter.
function hkdfExpand(prk, info, len) {
  const hashLen = 32;
  const n = Math.ceil(len / hashLen);
  let t = Buffer.alloc(0);
  let okm = Buffer.alloc(0);
  for (let i = 1; i <= n; i++) {
    t = crypto.createHmac('sha256', prk)
      .update(t).update(utf8(info)).update(Buffer.from([i]))
      .digest();
    okm = Buffer.concat([okm, t]);
  }
  return okm.subarray(0, len);
}

function encryptType2(data, encKey, macKey) {
  const iv = crypto.randomBytes(16);
  const cipher = crypto.createCipheriv('aes-256-cbc', encKey, iv); // PKCS7 padding is default
  const ct = Buffer.concat([cipher.update(data), cipher.final()]);
  const mac = crypto.createHmac('sha256', macKey).update(iv).update(ct).digest();
  return `2.${iv.toString('base64')}|${ct.toString('base64')}|${mac.toString('base64')}`;
}

function base64Url(buf) {
  return buf.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function postJson(url, body, headers = {}) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`POST ${url} -> ${res.status} ${res.statusText}: ${text}`);
  return text ? JSON.parse(text) : {};
}

// ---- key material -----------------------------------------------------------

const email = EMAIL.trim().toLowerCase();
const masterKey = pbkdf2(utf8(PASSWORD), utf8(email), ITER, 32);
const masterPasswordHash = pbkdf2(masterKey, utf8(PASSWORD), 1, 32).toString('base64');
const stretchEnc = hkdfExpand(masterKey, 'enc', 32);
const stretchMac = hkdfExpand(masterKey, 'mac', 32);

// 64-byte user symmetric key (32 enc || 32 mac), wrapped with the stretched key.
const userKey = crypto.randomBytes(64);
const protectedKey = encryptType2(userKey, stretchEnc, stretchMac);

// RSA keypair; private key is wrapped with the user key, public key sent as-is.
const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
const spkiDer = publicKey.export({ type: 'spki', format: 'der' });
const pkcs8Der = privateKey.export({ type: 'pkcs8', format: 'der' });
const encryptedPrivateKey = encryptType2(pkcs8Der, userKey.subarray(0, 32), userKey.subarray(32, 64));

// ---- 1. register ------------------------------------------------------------

const base = VW_URL.replace(/\/+$/, '');
log(`registering ${email} at ${base} (kdf iterations ${ITER})`);
await postJson(`${base}/identity/accounts/register`, {
  email,
  name: 'Smoke Test',
  masterPasswordHash,
  masterPasswordHint: null,
  key: protectedKey,
  kdf: 0,
  kdfIterations: ITER,
  keys: { publicKey: spkiDer.toString('base64'), encryptedPrivateKey },
});
log('registered');

// ---- 2. password-login to obtain an access token ---------------------------

const form = new URLSearchParams({
  grant_type: 'password',
  username: email,
  password: masterPasswordHash,
  scope: 'api offline_access',
  client_id: 'cli',
  deviceType: '21',
  deviceIdentifier: crypto.randomUUID(),
  deviceName: 'bootstrap',
});
const tokenRes = await fetch(`${base}/identity/connect/token`, {
  method: 'POST',
  headers: {
    'Content-Type': 'application/x-www-form-urlencoded',
    'Auth-Email': base64Url(utf8(email)),
  },
  body: form.toString(),
});
const tokenText = await tokenRes.text();
if (!tokenRes.ok) throw new Error(`login -> ${tokenRes.status}: ${tokenText}`);
const token = JSON.parse(tokenText);
const accessToken = token.access_token;
if (!accessToken) throw new Error(`login returned no access_token: ${tokenText}`);

// The personal API key client_id is "user.<userId>"; the user id is the JWT 'sub'.
const payload = JSON.parse(Buffer.from(accessToken.split('.')[1], 'base64').toString('utf8'));
const userId = payload.sub;
if (!userId) throw new Error('access token had no sub claim');
log(`logged in, user id ${userId}`);

// ---- 3. fetch (or create) the personal API key ------------------------------

const apiKeyRes = await postJson(`${base}/api/accounts/api-key`, { masterPasswordHash }, {
  Authorization: `Bearer ${accessToken}`,
});
const clientSecret = apiKeyRes.apiKey;
if (!clientSecret) throw new Error(`api-key response had no apiKey: ${JSON.stringify(apiKeyRes)}`);
log('retrieved personal API key');

// ---- 4. seed vault items ----------------------------------------------------
// Values are wrapped with the user symmetric key (the same key the adapter
// unwraps from the profile). Each seed key maps onto a different item shape so
// the tests cover all three of the adapter's value-resolution paths:
//   *-note-secret  -> secure note, value in notes
//   *-field-secret -> secure note, value in a custom "password" field
//   anything else  -> login item, value in login.password
const encU = (s) => encryptType2(utf8(s), userKey.subarray(0, 32), userKey.subarray(32, 64));
const authHeader = { Authorization: `Bearer ${accessToken}` };

const seed = JSON.parse(SMOKE_SEED_JSON || '{}');
for (const [key, value] of Object.entries(seed)) {
  let cipher;
  if (key.endsWith('note-secret')) {
    log(`  + secure note   ${key}`);
    cipher = { type: 2, name: encU(key), notes: encU(value), secureNote: { type: 0 } };
  } else if (key.endsWith('field-secret')) {
    log(`  + custom field  ${key}`);
    cipher = { type: 2, name: encU(key), notes: null, secureNote: { type: 0 },
               fields: [{ name: encU('password'), value: encU(value), type: 1 }] };
  } else {
    log(`  + login item    ${key}`);
    cipher = { type: 1, name: encU(key), notes: null,
               login: { username: encU('smoke'), password: encU(value), uris: null, totp: null } };
  }
  await postJson(`${base}/api/ciphers`, { favorite: false, folderId: null, organizationId: null, ...cipher }, authHeader);
}
log(`seeded ${Object.keys(seed).length} item(s)`);

process.stdout.write(JSON.stringify({ clientId: `user.${userId}`, clientSecret, userId }) + '\n');

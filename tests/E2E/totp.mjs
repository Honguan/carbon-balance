import crypto from "node:crypto";

export function totp(sharedKey) {
    const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    const normalized = sharedKey.replace(/[^A-Z2-7]/gi, "").toUpperCase();
    let bits = "";
    for (const character of normalized) {
        bits += alphabet.indexOf(character).toString(2).padStart(5, "0");
    }
    const key = Buffer.from(
        Array.from({ length: Math.floor(bits.length / 8) }, (_, index) =>
            Number.parseInt(bits.slice(index * 8, (index + 1) * 8), 2))
    );
    const counter = Buffer.alloc(8);
    counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)));
    const digest = crypto.createHmac("sha1", key).update(counter).digest();
    const offset = digest[digest.length - 1] & 0x0f;
    return ((digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, "0");
}

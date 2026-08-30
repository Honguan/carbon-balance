import { chromium } from "playwright";
import pg from "pg";

const baseUrl = process.env.CARBON_E2E_BASE_URL ?? "http://127.0.0.1:18090";
const postgresUrl = process.env.CARBON_E2E_PG_URL;

if (!postgresUrl) {
    throw new Error("CARBON_E2E_PG_URL is required.");
}

const client = new pg.Client({ connectionString: postgresUrl });
await client.connect();
const result = await client.query(`
    SELECT email
    FROM identity.users
    WHERE email_confirmed = TRUE
      AND email LIKE 'carbon-e2e-%@example.test'
    LIMIT 1`);
await client.end();

const knownEmail = result.rows[0]?.email;
if (!knownEmail) {
    throw new Error("No confirmed browser-test account was found.");
}

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();

async function requestReset(email) {
    await page.goto(`${baseUrl}/Identity/Account/ForgotPassword`, { waitUntil: "domcontentloaded" });
    await page.locator('input[name="Input.Email"]').fill(email);
    await page.locator('button[type="submit"]').click();
    await page.waitForLoadState("domcontentloaded");
    if (!page.url().includes("/Identity/Account/ForgotPasswordConfirmation")) {
        throw new Error(`Password reset exposed a distinct response for ${email}. Current URL: ${page.url()}`);
    }
}

try {
    await requestReset(`unknown-${Date.now()}@example.test`);
    await requestReset(knownEmail);
    console.log("Password reset remained enumeration-resistant during an SMTP outage.");
} finally {
    await browser.close();
}

import { chromium } from "playwright";
import pg from "pg";
import { totp } from "./totp.mjs";

const { Client } = pg;

const baseUrl = process.env.CARBON_E2E_BASE_URL ?? "http://127.0.0.1:18088";
const postgresUrl = process.env.CARBON_E2E_PG_URL;
if (!postgresUrl) {
    throw new Error("CARBON_E2E_PG_URL is required.");
}

const client = new Client({ connectionString: postgresUrl });
await client.connect();
const fixture = await client.query(
    `SELECT u.email, t.value AS authenticator_key
     FROM identity.users u
     JOIN identity.user_tokens t ON t.user_id = u.id
     WHERE u.two_factor_enabled
       AND u.email LIKE 'carbon-e2e-%@example.test'
       AND t.login_provider = '[AspNetUserStore]'
       AND t.name = 'AuthenticatorKey'
     ORDER BY u.email DESC
     LIMIT 1`
);
await client.end();
if (fixture.rowCount !== 1) {
    throw new Error("No completed MFA browser fixture was found.");
}

const { email, authenticator_key: sharedKey } = fixture.rows[0];
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const page = await context.newPage();

try {
    await page.goto(`${baseUrl}/Identity/Account/Login`, { waitUntil: "domcontentloaded" });
    await page.locator('input[name="Input.Identifier"]').fill(email);
    await page.locator('input[name="Input.Password"]').fill("carbon1");
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    if (!page.url().includes("/Identity/Account/LoginWith2fa")) {
        throw new Error(`Password login bypassed MFA: ${page.url()}`);
    }
    await page.locator('input[name="Input.TwoFactorCode"]').fill(totp(sharedKey));
    await page.locator("form button[type=submit]").click();
    await page.waitForURL("**/Workspace**");

    await page.locator("#invitationEmail").fill(`fresh-${Date.now()}@example.test`);
    await page.getByRole("button", { name: "寄送邀請" }).click();
    await page.getByText("組織邀請已寄出。").waitFor({ state: "visible" });

    await page.waitForTimeout(11_000);
    await page.locator("#invitationEmail").fill(`stale-${Date.now()}@example.test`);
    const staleResponsePromise = page.waitForResponse(
        (response) => response.url().includes("handler=InviteMember") && response.request().method() === "POST"
    );
    await page.getByRole("button", { name: "寄送邀請" }).click();
    const staleResponse = await staleResponsePromise;
    if (staleResponse.status() !== 302
        || !staleResponse.headers().location?.includes("/Identity/Account/AccessDenied")) {
        throw new Error(`Stale MFA session was accepted: ${staleResponse.status()} ${staleResponse.headers().location ?? ""}`);
    }

    console.log(`Stale MFA browser E2E passed for ${email}.`);
} finally {
    await context.close();
    await browser.close();
}

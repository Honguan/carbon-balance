import { chromium } from "playwright";
import fs from "node:fs/promises";
import path from "node:path";

const baseUrl = process.env.CARBON_E2E_BASE_URL ?? "http://127.0.0.1:18088";
const mailpitUrl = process.env.CARBON_E2E_MAILPIT_URL ?? "http://127.0.0.1:8025";
const artifactsDirectory = process.env.CARBON_E2E_ARTIFACTS ?? "/work/artifacts";
const testEmail = `carbon-e2e-${Date.now()}@example.test`;
const testPassword = "carbon1";

await fs.mkdir(artifactsDirectory, { recursive: true });

function assert(condition, message) {
    if (!condition) {
        throw new Error(message);
    }
}

async function expectUrl(page, suffix, message) {
    await page.waitForLoadState("domcontentloaded");
    assert(page.url().includes(suffix), `${message}. Current URL: ${page.url()}`);
}

async function expectVisible(locator, message) {
    await locator.waitFor({ state: "visible", timeout: 10_000 });
    const box = await locator.boundingBox();
    assert(box && box.width > 0 && box.height > 0, `${message}: element has no clickable area.`);
    const pointerEvents = await locator.evaluate((element) => getComputedStyle(element).pointerEvents);
    assert(pointerEvents !== "none", `${message}: pointer-events is none.`);
}

async function waitForLatestConfirmationLink(mailPage) {
    for (let attempt = 1; attempt <= 30; attempt += 1) {
        const response = await mailPage.goto(`${mailpitUrl}/view/latest.html`, {
            waitUntil: "domcontentloaded"
        });
        if (response?.ok()) {
            const confirmationLink = mailPage.getByRole("link", { name: "確認帳號" });
            if (await confirmationLink.count()) {
                const href = await confirmationLink.first().getAttribute("href");
                if (href) {
                    return href;
                }
            }
        }
        await mailPage.waitForTimeout(1_000);
    }

    throw new Error("Mailpit did not receive a usable confirmation email within 30 seconds.");
}

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const page = await context.newPage();
const browserErrors = [];

page.on("pageerror", (error) => browserErrors.push(`pageerror: ${error.message}`));
page.on("console", (message) => {
    if (message.type() === "error") {
        browserErrors.push(`console: ${message.text()}`);
    }
});

try {
    // Login page must remain a real login page and all account links must be clickable.
    let response = await page.goto(`${baseUrl}/Identity/Account/Login`, { waitUntil: "networkidle" });
    assert(response?.status() === 200, `Login page returned ${response?.status()}.`);
    await expectUrl(page, "/Identity/Account/Login", "Login page redirected without credentials");

    const registerLink = page.locator('a[href="/Identity/Account/Register"]');
    const resendLink = page.locator('a[href="/Identity/Account/ResendEmailConfirmation"]');
    await expectVisible(registerLink, "Register link is not clickable");
    await expectVisible(resendLink, "Resend confirmation link is not clickable");

    // Empty login must stay logged out and show validation feedback.
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    await expectUrl(page, "/Identity/Account/Login", "Empty login left the login page");
    await page.getByText("請輸入帳號或 Email。").waitFor({ state: "visible" });
    await page.getByText("請輸入密碼。").waitFor({ state: "visible" });

    response = await page.goto(`${baseUrl}/Workspace`, { waitUntil: "domcontentloaded" });
    assert(response?.status() === 200, `Anonymous workspace navigation ended with ${response?.status()}.`);
    await expectUrl(page, "/Identity/Account/Login", "Anonymous user reached the workspace");

    // Register link and previous-page navigation must work through real clicks.
    await registerLink.click();
    await expectUrl(page, "/Identity/Account/Register", "Register link did not navigate");
    await page.getByRole("heading", { name: "建立初始管理者" }).waitFor({ state: "visible" });
    await expectVisible(page.locator("[data-history-back]"), "Register back link is not clickable");
    await page.locator("[data-history-back]").click();
    await expectUrl(page, "/Identity/Account/Login", "Register back link did not return to login");

    // Resend link and previous-page navigation must also work.
    await page.locator('a[href="/Identity/Account/ResendEmailConfirmation"]').click();
    await expectUrl(page, "/Identity/Account/ResendEmailConfirmation", "Resend link did not navigate");
    await page.getByRole("heading", { name: "重寄確認信" }).waitFor({ state: "visible" });
    await expectVisible(page.locator("[data-history-back]"), "Resend back link is not clickable");
    await page.locator("[data-history-back]").click();
    await expectUrl(page, "/Identity/Account/Login", "Resend back link did not return to login");

    // Missing confirmation parameters must show a friendly page instead of HTTP 400.
    response = await page.goto(`${baseUrl}/Identity/Account/ConfirmEmail`, { waitUntil: "domcontentloaded" });
    assert(response?.status() === 200, `Incomplete confirmation link returned ${response?.status()}.`);
    await page.getByRole("heading", { name: "無法確認 Email" }).waitFor({ state: "visible" });
    await page.getByText("確認連結不完整").waitFor({ state: "visible" });

    // Register a real account.
    await page.goto(`${baseUrl}/Identity/Account/Register`, { waitUntil: "domcontentloaded" });
    await page.locator('input[name="Input.DisplayName"]').fill("E2E 碳盤查管理者");
    await page.locator('input[name="Input.Email"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.locator('input[name="Input.ConfirmPassword"]').fill(testPassword);
    await page.getByRole("button", { name: "建立帳號並寄送確認信" }).click();
    await expectUrl(page, "/Identity/Account/Login", "Registration did not return to login");
    await page.getByText("初始管理者已建立").waitFor({ state: "visible" });

    // The unconfirmed account must not be able to log in.
    await page.locator('input[name="Input.Identifier"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    await page.getByText("尚未完成 Email 確認").waitFor({ state: "visible" });
    await expectUrl(page, "/Identity/Account/Login", "Unconfirmed account left the login page");

    // Resend confirmation must submit successfully and remain usable.
    await page.locator('a[href="/Identity/Account/ResendEmailConfirmation"]').click();
    await page.locator('input[name="Input.Email"]').fill(testEmail);
    await page.getByRole("button", { name: "寄送確認信" }).click();
    await page.getByText("確認信已寄出").waitFor({ state: "visible" });

    // Follow the actual Mailpit confirmation link.
    const mailPage = await context.newPage();
    const confirmationHref = await waitForLatestConfirmationLink(mailPage);
    await page.goto(confirmationHref, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "Email 已確認" }).waitFor({ state: "visible" });
    await page.getByRole("link", { name: /前往登入/ }).click();
    await expectUrl(page, "/Identity/Account/Login", "Confirmation page did not return to login");
    await mailPage.close();

    // Persistent login must create a long-lived cookie and enter a working workspace.
    await page.locator('input[name="Input.Identifier"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.locator('input[name="Input.RememberMe"]').check();
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    await expectUrl(page, "/Workspace", "Confirmed account did not enter the workspace");
    await page.getByText("建立組織").first().waitFor({ state: "visible" });

    const persistentCookie = (await context.cookies(baseUrl)).find((cookie) => cookie.name.includes("Identity.Application"));
    assert(persistentCookie, "Authentication cookie was not created.");
    const minimumPersistentExpiry = Math.floor(Date.now() / 1000) + (29 * 24 * 60 * 60);
    assert(
        persistentCookie.expires >= minimumPersistentExpiry,
        `Remember-me cookie expires too early: ${persistentCookie.expires}.`
    );

    // The first authenticated workspace action must work, not end in HTTP 400.
    await page.locator("#organizationName").fill("E2E 碳足跡組織");
    await page.getByRole("button", { name: "建立組織" }).click();
    await expectUrl(page, "/Workspace", "Organization creation left the workspace");
    await page.getByText("組織已建立。").waitFor({ state: "visible" });
    await page.getByText("目前組織：").waitFor({ state: "visible" });

    // Opening the login URL explicitly must clear the previous session instead of silently logging in.
    await page.goto(`${baseUrl}/Identity/Account/Login`, { waitUntil: "domcontentloaded" });
    await expectUrl(page, "/Identity/Account/Login", "Explicit login page visit redirected into the app");
    await page.getByText("已清除原有登入狀態").waitFor({ state: "visible" });
    const remainingAuthCookie = (await context.cookies(baseUrl)).find((cookie) => cookie.name.includes("Identity.Application"));
    assert(!remainingAuthCookie, "Explicit login page visit did not clear the old authentication cookie.");

    // A login without remember-me must create only a session cookie.
    await page.locator('input[name="Input.Identifier"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    await expectUrl(page, "/Workspace", "Session login did not reach the workspace");
    const sessionCookie = (await context.cookies(baseUrl)).find((cookie) => cookie.name.includes("Identity.Application"));
    assert(sessionCookie, "Session authentication cookie was not created.");
    assert(sessionCookie.expires === -1, `Unchecked remember-me created a persistent cookie: ${sessionCookie.expires}.`);

    // Logout must work through the real navigation form.
    await page.getByRole("button", { name: "登出" }).click();
    await expectUrl(page, "/", "Logout did not return to the home page");
    response = await page.goto(`${baseUrl}/Workspace`, { waitUntil: "domcontentloaded" });
    assert(response?.status() === 200, `Post-logout workspace navigation ended with ${response?.status()}.`);
    await expectUrl(page, "/Identity/Account/Login", "Logged-out user reached the workspace");

    // A POST without an anti-forgery token must recover to a usable login page, not expose HTTP 400.
    const invalidPost = await context.request.post(`${baseUrl}/Identity/Account/Login`, {
        form: {
            "Input.Identifier": testEmail,
            "Input.Password": testPassword
        },
        maxRedirects: 0
    });
    assert(invalidPost.status() === 302, `Invalid form POST returned ${invalidPost.status()} instead of a recovery redirect.`);
    assert(
        invalidPost.headers().location?.includes("requestExpired=true"),
        `Invalid form POST redirected to an unexpected location: ${invalidPost.headers().location}`
    );

    assert(browserErrors.length === 0, `Browser errors detected:\n${browserErrors.join("\n")}`);
    await page.screenshot({ path: path.join(artifactsDirectory, "auth-flow-success.png"), fullPage: true });
    console.log(`Authentication browser E2E passed for ${testEmail}.`);
} catch (error) {
    await page.screenshot({ path: path.join(artifactsDirectory, "auth-flow-failure.png"), fullPage: true }).catch(() => {});
    await fs.writeFile(
        path.join(artifactsDirectory, "browser-errors.txt"),
        `${error.stack ?? error}\n\n${browserErrors.join("\n")}`
    );
    throw error;
} finally {
    await context.close();
    await browser.close();
}

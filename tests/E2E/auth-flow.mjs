import { chromium } from "playwright";
import fs from "node:fs/promises";
import path from "node:path";

const baseUrl = process.env.CARBON_E2E_BASE_URL ?? "http://127.0.0.1:18088";
const mailpitUrl = process.env.CARBON_E2E_MAILPIT_URL ?? "http://127.0.0.1:8025";
const artifactsDirectory = process.env.CARBON_E2E_ARTIFACTS ?? "/work/artifacts";
const postgresUrl = process.env.CARBON_E2E_PG_URL;
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

async function withDatabase(callback) {
    assert(postgresUrl, "CARBON_E2E_PG_URL is required for workspace fixture setup.");
    const pgModule = await import("pg");
    const Client = pgModule.Client ?? pgModule.default.Client;
    const client = new Client({ connectionString: postgresUrl });
    await client.connect();
    try {
        return await callback(client);
    } finally {
        await client.end();
    }
}

async function publishPcrFixture(registrationNumber) {
    const rowCount = await withDatabase(async (client) => {
        const result = await client.query(
            `UPDATE app.pcr_versions
             SET "ReviewStatus" = 'Approved',
                 "PublicationStatus" = 'Published',
                 "ReviewedAt" = NOW(),
                 "PublishedAt" = NOW()
             WHERE "RegistrationNumber" = $1`,
            [registrationNumber]
        );
        return result.rowCount;
    });
    assert(rowCount === 1, `Expected one PCR fixture to publish, updated ${rowCount}.`);
}

async function publishFactorFixture(factorName) {
    const rowCount = await withDatabase(async (client) => {
        const result = await client.query(
            `UPDATE app.emission_factor_versions
             SET "ReviewStatus" = 'Approved',
                 "PublicationStatus" = 'Published',
                 "ReviewedAt" = NOW(),
                 "PublishedAt" = NOW()
             WHERE "Name" = $1`,
            [factorName]
        );
        return result.rowCount;
    });
    assert(rowCount === 1, `Expected one factor fixture to publish, updated ${rowCount}.`);
}

async function expectSelectOptions(select, minimumOptions, message) {
    await expectVisible(select, message);
    assert(!(await select.isDisabled()), `${message}: select is disabled.`);
    const optionCount = await select.locator("option").count();
    assert(optionCount >= minimumOptions, `${message}: expected at least ${minimumOptions} options, got ${optionCount}.`);
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
const responseLog = [];

page.on("pageerror", (error) => browserErrors.push(`pageerror: ${error.message}`));
page.on("console", (message) => {
    if (message.type() === "error") {
        browserErrors.push(`console: ${message.text()}`);
    }
});
page.on("response", (response) => {
    if (response.url().includes("/Identity/") || response.url().includes("/Workspace")) {
        responseLog.push(`${response.status()} ${response.request().method()} ${response.url()}`);
    }
});

try {
    let response = await page.goto(`${baseUrl}/Identity/Account/Login`, { waitUntil: "networkidle" });
    assert(response?.status() === 200, `Login page returned ${response?.status()}.`);
    await expectUrl(page, "/Identity/Account/Login", "Login page redirected without credentials");

    const registerLink = page.locator('a[href="/Identity/Account/Register"]');
    const resendLink = page.locator('a[href="/Identity/Account/ResendEmailConfirmation"]');
    await expectVisible(registerLink, "Register link is not clickable");
    await expectVisible(resendLink, "Resend confirmation link is not clickable");

    const loginResponsePromise = page.waitForResponse(
        (candidate) => candidate.url().includes("/Identity/Account/Login") && candidate.request().method() === "POST",
        { timeout: 10_000 }
    );
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    const emptyLoginResponse = await loginResponsePromise;
    assert(emptyLoginResponse.status() === 200, `Empty login returned ${emptyLoginResponse.status()}.`);
    await expectUrl(page, "/Identity/Account/Login", "Empty login left the login page");
    await page.getByText("請輸入帳號或 Email。").waitFor({ state: "visible", timeout: 10_000 });
    await page.getByText("請輸入密碼。").waitFor({ state: "visible", timeout: 10_000 });

    response = await page.goto(`${baseUrl}/Workspace`, { waitUntil: "domcontentloaded" });
    assert(response?.status() === 200, `Anonymous workspace navigation ended with ${response?.status()}.`);
    await expectUrl(page, "/Identity/Account/Login", "Anonymous user reached the workspace");

    await registerLink.click();
    await expectUrl(page, "/Identity/Account/Register", "Register link did not navigate");
    await page.getByRole("heading", { name: "建立初始管理者" }).waitFor({ state: "visible" });
    await expectVisible(page.locator("[data-history-back]"), "Register back link is not clickable");
    await page.locator("[data-history-back]").click();
    await expectUrl(page, "/Identity/Account/Login", "Register back link did not return to login");

    await page.locator('a[href="/Identity/Account/ResendEmailConfirmation"]').click();
    await expectUrl(page, "/Identity/Account/ResendEmailConfirmation", "Resend link did not navigate");
    await page.getByRole("heading", { name: "重寄確認信" }).waitFor({ state: "visible" });
    await expectVisible(page.locator("[data-history-back]"), "Resend back link is not clickable");
    await page.locator("[data-history-back]").click();
    await expectUrl(page, "/Identity/Account/Login", "Resend back link did not return to login");

    response = await page.goto(`${baseUrl}/Identity/Account/ConfirmEmail`, { waitUntil: "domcontentloaded" });
    assert(response?.status() === 200, `Incomplete confirmation link returned ${response?.status()}.`);
    await page.getByRole("heading", { name: "無法確認 Email" }).waitFor({ state: "visible" });
    await page.getByText("確認連結不完整").waitFor({ state: "visible" });

    await page.goto(`${baseUrl}/Identity/Account/Register`, { waitUntil: "domcontentloaded" });
    await page.locator('input[name="Input.DisplayName"]').fill("E2E 碳盤查管理者");
    await page.locator('input[name="Input.Email"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.locator('input[name="Input.ConfirmPassword"]').fill(testPassword);
    await page.getByRole("button", { name: "建立帳號並寄送確認信" }).click();
    await expectUrl(page, "/Identity/Account/Login", "Registration did not return to login");
    await page.getByText("初始管理者已建立").waitFor({ state: "visible" });

    await page.locator('input[name="Input.Identifier"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    await page.getByText("尚未完成 Email 確認").waitFor({ state: "visible" });
    await expectUrl(page, "/Identity/Account/Login", "Unconfirmed account left the login page");

    await page.locator('a[href="/Identity/Account/ResendEmailConfirmation"]').click();
    await page.locator('input[name="Input.Email"]').fill(testEmail);
    await page.getByRole("button", { name: "寄送確認信" }).click();
    await page.getByText("確認信已寄出").waitFor({ state: "visible" });

    const mailPage = await context.newPage();
    const confirmationHref = await waitForLatestConfirmationLink(mailPage);
    await page.goto(confirmationHref, { waitUntil: "domcontentloaded" });
    await page.getByRole("heading", { name: "Email 已確認" }).waitFor({ state: "visible" });
    await page.getByRole("link", { name: /前往登入/ }).click();
    await expectUrl(page, "/Identity/Account/Login", "Confirmation page did not return to login");
    await mailPage.close();

    await page.locator('input[name="Input.Identifier"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.getByRole("checkbox", { name: "在這台裝置保持登入 30 天" }).check();
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

    await page.locator("#organizationName").fill("E2E 碳足跡組織");
    await page.getByRole("button", { name: "建立組織" }).click();
    await expectUrl(page, "/Workspace", "Organization creation left the workspace");
    await page.getByText("組織已建立。").waitFor({ state: "visible" });
    await page.getByText("目前組織", { exact: true }).waitFor({ state: "visible" });


    if (postgresUrl) {
    // Empty dependent selects must explain the missing prerequisite instead of appearing broken.
    await page.goto(`${baseUrl}/Workspace/product`, { waitUntil: "networkidle" });
    const emptyFacilitySelect = page.locator("#facilityId");
    assert(await emptyFacilitySelect.isDisabled(), "Facility select should be disabled before a facility exists.");
    assert((await emptyFacilitySelect.locator("option").first().textContent()).includes("尚無可選廠場"), "Empty facility select did not explain its prerequisite.");
    await page.locator("#facilityId-help a").click();
    await expectUrl(page, "/Workspace/governance", "Facility prerequisite link did not open governance");

    await page.locator("#facilityCode").fill("TW-E2E");
    await page.locator("#facilityName").fill("E2E 測試廠場");
    await page.getByRole("button", { name: "建立廠場" }).click();
    await page.getByText("廠場已建立。").waitFor({ state: "visible" });

    await page.goto(`${baseUrl}/Workspace/product`, { waitUntil: "networkidle" });
    const facilitySelect = page.locator("#facilityId");
    await expectSelectOptions(facilitySelect, 2, "Facility select did not become usable");
    await facilitySelect.selectOption({ label: "TW-E2E - E2E 測試廠場" });
    assert(await facilitySelect.inputValue(), "Facility selection did not retain a value.");

    await page.locator("#productName").fill("E2E 產品 A");
    await page.locator("#categoryCode").fill("E2E-A");
    await page.getByRole("button", { name: "儲存產品第 1 版" }).click();
    await page.getByText("產品與第 1 版已建立。").waitFor({ state: "visible" });

    await page.locator("#productName").fill("E2E 產品 B");
    await page.locator("#categoryCode").fill("E2E-B");
    await page.locator("#facilityId").selectOption({ label: "TW-E2E - E2E 測試廠場" });
    await page.getByRole("button", { name: "儲存產品第 1 版" }).click();
    await page.getByText("產品與第 1 版已建立。").waitFor({ state: "visible" });

    // Product versions are immediately selectable, while draft PCR records are deliberately excluded.
    await page.goto(`${baseUrl}/Workspace/inventory`, { waitUntil: "networkidle" });
    await expectSelectOptions(page.locator("#productVersionId"), 3, "Product-version select is unusable");
    const emptyPcrSelect = page.locator("#pcrVersionId");
    assert(await emptyPcrSelect.isDisabled(), "PCR select should be disabled without a published PCR.");
    assert((await emptyPcrSelect.locator("option").first().textContent()).includes("尚無已發布 PCR 版本"), "Empty PCR select did not explain its prerequisite.");

    const pcrRegistration = `PCR-E2E-${Date.now()}`;
    await page.goto(`${baseUrl}/Workspace/pcr`, { waitUntil: "networkidle" });
    await page.locator("#registrationNumber").fill(pcrRegistration);
    await page.locator("#pcrTitle").fill("E2E PCR 規範");
    await page.locator("#pcrVersionNumber").fill("1");
    await page.locator("#pcrValidFrom").fill("2025-01-01");
    await page.locator("#pcrValidTo").fill("2027-12-31");
    await page.locator("#sourceReference").fill("E2E controlled source");
    await page.locator("#standardCode").fill("ISO 14067");
    await page.locator("#cccClassification").fill("E2E-CCC");
    await page.locator("#pcrApplicability").fill("E2E products");
    await page.locator("#ruleRequirements").fill("E2E rules");
    await page.locator("#originalDocumentName").fill("e2e-pcr.pdf");
    await page.locator("#originalDocumentSha256").fill("a".repeat(64));
    await page.getByRole("button", { name: "儲存 PCR 草稿" }).click();
    await page.getByText("PCR 草稿已建立").waitFor({ state: "visible" });
    await publishPcrFixture(pcrRegistration);

    async function createInventory(productLabel, functionalUnit) {
        await page.goto(`${baseUrl}/Workspace/inventory`, { waitUntil: "networkidle" });
        const productSelect = page.locator("#productVersionId");
        const pcrSelect = page.locator("#pcrVersionId");
        await expectSelectOptions(productSelect, 3, "Product-version select failed after reload");
        await expectSelectOptions(pcrSelect, 2, "Published PCR did not become selectable");
        await productSelect.selectOption({ label: `${productLabel} v1` });
        await pcrSelect.selectOption({ label: `${pcrRegistration} v1 - E2E PCR 規範` });
        await page.locator("#functionalUnit").fill(functionalUnit);
        await page.getByRole("button", { name: "儲存盤查第 1 版" }).click();
        await page.getByText("盤查專案第 1 版已建立。").waitFor({ state: "visible" });
    }

    await createInventory("E2E 產品 A", "1 件 E2E 產品 A");
    await createInventory("E2E 產品 B", "1 件 E2E 產品 B");

    // Draft factors are not offered; once published, every lifecycle factor select becomes usable.
    await page.goto(`${baseUrl}/Workspace/lifecycle`, { waitUntil: "networkidle" });
    const emptyFactorSelect = page.locator('select[name="factorVersionId"]').first();
    assert(await emptyFactorSelect.isDisabled(), "Factor select should be disabled without a published factor.");
    assert((await emptyFactorSelect.locator("option").first().textContent()).includes("尚無已發布排放係數"), "Empty factor select did not explain its prerequisite.");

    const factorName = `E2E 排放係數 ${Date.now()}`;
    await page.goto(`${baseUrl}/Workspace/factors`, { waitUntil: "networkidle" });
    await expectSelectOptions(page.locator("#denominatorUnitCode"), 4, "Denominator-unit select is unusable");
    await page.locator("#factorName").fill(factorName);
    await page.locator("#factorValue").fill("1.25");
    await page.locator("#denominatorUnitCode").selectOption("kg");
    await page.locator("#sourceDatasetVersion").fill("e2e-dataset-v1");
    await page.locator("#licenseCode").fill("e2e-license");
    await page.locator("#factorSourceName").fill("E2E source");
    await page.locator("#datasetName").fill("E2E dataset");
    await page.locator("#factorApplicability").fill("E2E inventory period");
    await page.getByRole("button", { name: "建立係數草稿" }).click();
    await page.getByText("係數草稿已建立").waitFor({ state: "visible" });
    await publishFactorFixture(factorName);

    await page.goto(`${baseUrl}/Workspace/lifecycle`, { waitUntil: "networkidle" });
    const projectSelect = page.locator("#projectVersionId");
    await expectSelectOptions(projectSelect, 2, "Lifecycle project-version select is unusable");
    const projectValues = await projectSelect.locator("option").evaluateAll((options) => options.map((option) => option.value));
    const originalProjectValue = await projectSelect.inputValue();
    const alternateProjectValue = projectValues.find((value) => value !== originalProjectValue);
    assert(alternateProjectValue, "A second inventory project option was not available.");
    await Promise.all([
        page.waitForURL((url) => url.pathname.includes("/Workspace/lifecycle") && url.searchParams.get("projectVersionId") === alternateProjectValue),
        projectSelect.selectOption(alternateProjectValue)
    ]);

    const rawUnitSelect = page.locator('select[name="rawUnitCode"]').first();
    const canonicalUnitSelect = page.locator('select[name="canonicalUnitCode"]').first();
    const publishedFactorSelect = page.locator('select[name="factorVersionId"]').first();
    await expectSelectOptions(rawUnitSelect, 4, "Raw-unit select is unusable");
    await expectSelectOptions(canonicalUnitSelect, 4, "Canonical-unit select is unusable");
    await expectSelectOptions(publishedFactorSelect, 2, "Published factor select is unusable");
    await rawUnitSelect.selectOption("kg");
    await canonicalUnitSelect.selectOption("kg");
    await publishedFactorSelect.selectOption({ label: `${factorName} / kgCO2e／kg / e2e-dataset-v1` });
    await page.locator("#activity-raw-material").fill("E2E 原物料");
    await page.locator("#value-raw-material").fill("2");
    await page.getByRole("button", { name: "儲存「原料取得」活動" }).click();
    await page.getByText("活動數據已保存。").waitFor({ state: "visible" });

    await page.goto(`${baseUrl}/Workspace/calculation`, { waitUntil: "networkidle" });
    const calculationProjectSelect = page.locator("#calculationProjectVersionId");
    await expectSelectOptions(calculationProjectSelect, 2, "Calculation project-version select is unusable");
    const calculationValues = await calculationProjectSelect.locator("option").evaluateAll((options) => options.map((option) => option.value));
    const calculationCurrent = await calculationProjectSelect.inputValue();
    const calculationAlternate = calculationValues.find((value) => value !== calculationCurrent);
    assert(calculationAlternate, "Calculation selector did not expose a second inventory project.");
    await Promise.all([
        page.waitForURL((url) => url.pathname.includes("/Workspace/calculation") && url.searchParams.get("projectVersionId") === calculationAlternate),
        calculationProjectSelect.selectOption(calculationAlternate)
    ]);

    }

    await page.goto(`${baseUrl}/Identity/Account/Login`, { waitUntil: "domcontentloaded" });
    await expectUrl(page, "/Identity/Account/Login", "Explicit login page visit redirected into the app");
    await page.getByText("已清除原有登入狀態").waitFor({ state: "visible" });
    const remainingAuthCookie = (await context.cookies(baseUrl)).find((cookie) => cookie.name.includes("Identity.Application"));
    assert(!remainingAuthCookie, "Explicit login page visit did not clear the old authentication cookie.");

    await page.locator('input[name="Input.Identifier"]').fill(testEmail);
    await page.locator('input[name="Input.Password"]').fill(testPassword);
    await page.getByRole("button", { name: "登入碳足跡系統" }).click();
    await expectUrl(page, "/Workspace", "Session login did not reach the workspace");
    const sessionCookie = (await context.cookies(baseUrl)).find((cookie) => cookie.name.includes("Identity.Application"));
    assert(sessionCookie, "Session authentication cookie was not created.");
    assert(sessionCookie.expires === -1, `Unchecked remember-me created a persistent cookie: ${sessionCookie.expires}.`);

    await page.getByRole("button", { name: "登出" }).click();
    await expectUrl(page, "/", "Logout did not return to the home page");
    response = await page.goto(`${baseUrl}/Workspace`, { waitUntil: "domcontentloaded" });
    assert(response?.status() === 200, `Post-logout workspace navigation ended with ${response?.status()}.`);
    await expectUrl(page, "/Identity/Account/Login", "Logged-out user reached the workspace");

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
    await fs.writeFile(path.join(artifactsDirectory, "response-log.txt"), responseLog.join("\n"));
    console.log(`Authentication browser E2E passed for ${testEmail}.`);
} catch (error) {
    await page.screenshot({ path: path.join(artifactsDirectory, "auth-flow-failure.png"), fullPage: true }).catch(() => {});
    await fs.writeFile(path.join(artifactsDirectory, "failure-page.html"), await page.content().catch(() => ""));
    await fs.writeFile(path.join(artifactsDirectory, "response-log.txt"), responseLog.join("\n"));
    await fs.writeFile(
        path.join(artifactsDirectory, "browser-errors.txt"),
        `${error.stack ?? error}\n\n${browserErrors.join("\n")}`
    );
    throw error;
} finally {
    await context.close();
    await browser.close();
}

from pathlib import Path


def rep(path_text, old, new):
    path = Path(path_text)
    text = path.read_text()
    if old not in text:
        raise SystemExit(f"target not found: {path_text}: {old[:80]}")
    path.write_text(text.replace(old, new, 1))

rep(
    "src/CarbonFootprint.Web/wwwroot/js/site.js",
    '''    document.querySelectorAll("select[data-auto-submit-select]").forEach((select) => {
''',
    '''    document.querySelectorAll("select[data-controlled-other]").forEach((select) => {
        const target = document.querySelector(select.dataset.otherTarget ?? "");
        if (!(target instanceof HTMLInputElement) && !(target instanceof HTMLTextAreaElement)) {
            return;
        }
        const sync = () => {
            const enabled = select.value === "__other__";
            target.hidden = !enabled;
            target.required = enabled;
            if (!enabled) target.value = "";
        };
        select.addEventListener("change", sync);
        sync();
    });

    document.querySelectorAll("select[data-auto-submit-select]").forEach((select) => {
''')

rep(
    "tests/E2E/auth-flow.mjs",
    '''    await page.locator("#factorValue").fill("1.25");
    await page.locator("#denominatorUnitCode").selectOption("kg");
    await page.locator("#sourceDatasetVersion").fill("e2e-dataset-v1");
    await page.locator("#licenseCode").fill("e2e-license");
    await page.locator("#factorSourceName").fill("E2E source");
    await page.locator("#datasetName").fill("E2E dataset");
''',
    '''    await page.locator("#factorValue").fill("1.25");
    await page.locator("#denominatorUnitCode").selectOption("kg");
    await page.locator("#factorSourceType").selectOption("government-database");
    await page.locator("#factorGeography").selectOption("TW");
    await page.locator("#factorValidFrom").fill("2025-01-01");
    await page.locator("#factorValidTo").fill("2027-12-31");
    await page.locator("#sourceDatasetVersion").fill("e2e-dataset-v1");
    await page.locator("#licenseCode").fill("e2e-license");
    await page.locator("#factorSourceName").fill("E2E source");
    await page.locator("#factorSourceReference").fill("https://example.invalid/e2e-factor");
    await page.locator("#datasetName").fill("E2E dataset");
    await page.locator("#factorOriginalDocumentName").fill("e2e-factor-source.pdf");
    await page.locator("#factorOriginalDocumentSha256").fill("b".repeat(64));
''')

rep(
    "tests/E2E/auth-flow.mjs",
    '''    await publishedFactorSelect.selectOption(publishedFactorValue);
    await page.locator("#activity-raw-material").fill("E2E 原物料");
    await page.locator("#value-raw-material").fill("2");
''',
    '''    await publishedFactorSelect.selectOption(publishedFactorValue);
    await page.locator("#activity-raw-material").selectOption("主要原物料");
    await page.locator("#source-type-raw-material").selectOption("一級數據－供應商提供");
    await page.locator("#provider-raw-material").selectOption("供應商");
    await page.locator("#collection-raw-material").selectOption("供應商聲明／問卷");
    await page.locator("#source-reference-raw-material").fill("E2E-SUPPLIER-001");
    await page.locator("#value-raw-material").fill("2");
''')

Path("docs/15_SOURCE_EVIDENCE_AND_CONTROLLED_INPUTS.md").write_text(
'''# 來源證據與受控選單輸入設計

## 決策依據

雲端專題報告與論文提供五階段、能資源分配、廢棄物、運輸及使用情境的歷史需求；正式規則仍以現行法規、ISO 與有效 PCR 為優先。

原始文件 SHA-256 為必要完整性欄位，適用於 PCR 原始 PDF、排放係數資料集／文獻／報告、活動佐證檔案、匯入批次及不可變計算輸入。SHA-256 必須由原始檔案位元組計算，保存為 64 位小寫十六進位字串；網址與檔名不可代替。

## 每筆活動的必要資料

- 生命週期階段、活動類型與活動項目。
- 原始值／單位、標準值／單位及換算版本。
- 資料期間。
- 資料來源類型、提供者、取得方式與來源參照。
- 排放係數版本、分配比例與資料品質。
- 估算／替代資料理由。
- 佐證文件名稱、儲存位置、掃描狀態與 SHA-256。

## 設備簡化原則

設備不是所有活動的必填主體。製造階段先記錄能資源、廢棄物或委外運輸；設備只提供選填類別：生產機台、空壓機、鍋爐、冰水／冷凍、空調、泵浦／馬達、照明或其他。

品牌、型號、額定功率及效率不設為共通必填。只有 PCR、分配方法或佐證確實要求時，才在情境說明或後續設備子模組保存。

## 選單與其他輸入

穩定分類使用下拉選單，並提供「其他（自行輸入）」。選擇其他時才顯示自訂欄位；後端仍會重新驗證。原始文件 SHA-256 不提供略過選項。
''')

from pathlib import Path

path = Path("src/CarbonFootprint.Web/Pages/Workspace.cshtml")
text = path.read_text()
old = '''                                <div class="lifecycle-field">
                                    <label class="form-label" for="sourceDatasetVersion">來源資料集版本</label>
                                    <input class="form-control" id="sourceDatasetVersion" name="sourceDatasetVersion" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="licenseCode">授權識別</label>
                                    <input class="form-control" id="licenseCode" name="licenseCode" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorSourceName">來源機構／文件</label>
                                    <input class="form-control" id="factorSourceName" name="factorSourceName" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="datasetName">資料集名稱</label>
                                    <input class="form-control" id="datasetName" name="datasetName" required />
                                </div>
                                <div class="lifecycle-field lifecycle-field--wide">
                                    <label class="form-label" for="factorApplicability">適用性</label>
                                    <textarea class="form-control" id="factorApplicability" name="factorApplicability" required></textarea>
                                </div>
'''
new = '''                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorSourceType">來源類型</label>
                                    <select class="form-select" id="factorSourceType" name="factorSourceType" required data-controlled-other data-other-target="#factorSourceTypeOther">
                                        <option value="government-database">主管機關／政府資料庫</option>
                                        <option value="lca-database">國際 LCA 資料庫</option>
                                        <option value="academic-paper">學術文獻／論文</option>
                                        <option value="test-report">檢測／查驗報告</option>
                                        <option value="supplier-data">供應商一級資料</option>
                                        <option value="self-calculated">自廠計算係數</option>
                                        <option value="__other__">其他（自行輸入）</option>
                                    </select>
                                    <input class="form-control mt-2" id="factorSourceTypeOther" name="factorSourceTypeOther" placeholder="請輸入其他來源類型" hidden />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorGeography">適用地域</label>
                                    <select class="form-select" id="factorGeography" name="factorGeography" required data-controlled-other data-other-target="#factorGeographyOther">
                                        <option value="TW">台灣</option>
                                        <option value="Global">全球</option>
                                        <option value="East Asia">東亞</option>
                                        <option value="EU">歐盟</option>
                                        <option value="US">美國</option>
                                        <option value="CN">中國</option>
                                        <option value="JP">日本</option>
                                        <option value="__other__">其他（自行輸入）</option>
                                    </select>
                                    <input class="form-control mt-2" id="factorGeographyOther" name="factorGeographyOther" placeholder="請輸入其他地域" hidden />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorValidFrom">有效起日</label>
                                    <input class="form-control" type="date" id="factorValidFrom" name="factorValidFrom" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorValidTo">有效迄日</label>
                                    <input class="form-control" type="date" id="factorValidTo" name="factorValidTo" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="sourceDatasetVersion">來源資料集版本</label>
                                    <input class="form-control" id="sourceDatasetVersion" name="sourceDatasetVersion" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="licenseCode">授權識別</label>
                                    <input class="form-control" id="licenseCode" name="licenseCode" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorSourceName">來源機構／文件</label>
                                    <input class="form-control" id="factorSourceName" name="factorSourceName" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorSourceReference">來源 URL／文件編號</label>
                                    <input class="form-control" id="factorSourceReference" name="factorSourceReference" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="datasetName">資料集名稱</label>
                                    <input class="form-control" id="datasetName" name="datasetName" required />
                                </div>
                                <div class="lifecycle-field">
                                    <label class="form-label" for="factorOriginalDocumentName">原始文件名稱</label>
                                    <input class="form-control" id="factorOriginalDocumentName" name="factorOriginalDocumentName" required />
                                </div>
                                <div class="lifecycle-field lifecycle-field--wide">
                                    <label class="form-label" for="factorOriginalDocumentSha256">原始文件 SHA-256</label>
                                    <input class="form-control" id="factorOriginalDocumentSha256" name="factorOriginalDocumentSha256" pattern="[0-9a-fA-F]{64}" autocomplete="off" required />
                                    <p class="workspace-select-help">請填原始下載或匯入檔案的 64 位 SHA-256；不可用網址或檔名代替。</p>
                                </div>
                                <div class="lifecycle-field lifecycle-field--wide">
                                    <label class="form-label" for="factorApplicability">適用性</label>
                                    <textarea class="form-control" id="factorApplicability" name="factorApplicability" required></textarea>
                                </div>
'''
if old not in text:
    raise SystemExit("factor form target not found")
text = text.replace(old, new, 1)
text = text.replace(
    '<span>@factor.Value kgCO2e／@factor.DenominatorUnitCode／@factor.SourceDatasetVersion／@factor.PublicationStatus</span>',
    '<span>@factor.Value kgCO2e／@factor.DenominatorUnitCode／@factor.SourceDatasetVersion／@factor.PublicationStatus</span>\n                                    <span>來源：@factor.SourceType／@factor.Geography／@factor.SourceReference</span>\n                                    <span>原始文件：@factor.OriginalDocumentName／<code>@factor.OriginalDocumentSha256</code></span>',
    1)
path.write_text(text)

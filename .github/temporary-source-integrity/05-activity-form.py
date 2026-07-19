from pathlib import Path

path = Path("src/CarbonFootprint.Web/Pages/Workspace.cshtml")
text = path.read_text()
old = '''                                        <div class="lifecycle-field">
                                            <label class="form-label" for="activity-@stageSection.Slug">@stageSection.ActivityLabel</label>
                                            <input class="form-control" id="activity-@stageSection.Slug" name="activityName" required maxlength="300" />
                                        </div>
                                        <div class="lifecycle-field lifecycle-field--wide">
                                            <label class="form-label" for="scenario-@stageSection.Slug">@stageSection.ScenarioLabel</label>
                                            <input class="form-control" id="scenario-@stageSection.Slug" name="supplierOrScenario" maxlength="1000" />
                                        </div>
'''
new = '''                                        <div class="lifecycle-field">
                                            <label class="form-label" for="activity-@stageSection.Slug">@stageSection.ActivityLabel</label>
                                            <select class="form-select" id="activity-@stageSection.Slug" name="activityName" required data-controlled-other data-other-target="#activity-other-@stageSection.Slug">
                                                @if (stageSection.Stage == LifecycleStage.RawMaterial)
                                                {
                                                    <option value="主要原物料">主要原物料</option>
                                                    <option value="包裝材料">包裝材料</option>
                                                    <option value="輔助材料">輔助材料</option>
                                                    <option value="供應商運輸">供應商運輸</option>
                                                }
                                                else if (stageSection.Stage == LifecycleStage.Manufacturing)
                                                {
                                                    <option value="市電">市電</option>
                                                    <option value="自發電力">自發電力</option>
                                                    <option value="天然氣／燃料">天然氣／燃料</option>
                                                    <option value="蒸汽">蒸汽</option>
                                                    <option value="用水">用水</option>
                                                    <option value="冷媒">冷媒</option>
                                                    <option value="製造廢棄物">製造廢棄物</option>
                                                    <option value="委外處理運輸">委外處理運輸</option>
                                                }
                                                else if (stageSection.Stage == LifecycleStage.Distribution)
                                                {
                                                    <option value="公路運輸">公路運輸</option>
                                                    <option value="鐵路運輸">鐵路運輸</option>
                                                    <option value="海運">海運</option>
                                                    <option value="空運">空運</option>
                                                }
                                                else if (stageSection.Stage == LifecycleStage.Use)
                                                {
                                                    <option value="使用電力">使用電力</option>
                                                    <option value="使用用水">使用用水</option>
                                                    <option value="使用燃料">使用燃料</option>
                                                    <option value="耗材／維護">耗材／維護</option>
                                                }
                                                else
                                                {
                                                    <option value="回收">回收</option>
                                                    <option value="焚化">焚化</option>
                                                    <option value="掩埋">掩埋</option>
                                                    <option value="廢棄物運輸">廢棄物運輸</option>
                                                }
                                                <option value="__other__">其他（自行輸入）</option>
                                            </select>
                                            <input class="form-control mt-2" id="activity-other-@stageSection.Slug" name="activityNameOther" placeholder="請輸入其他活動項目" maxlength="300" hidden />
                                        </div>
                                        <div class="lifecycle-field lifecycle-field--wide">
                                            <label class="form-label" for="scenario-@stageSection.Slug">@stageSection.ScenarioLabel</label>
                                            <input class="form-control" id="scenario-@stageSection.Slug" name="supplierOrScenario" maxlength="1000" />
                                        </div>
                                        @if (stageSection.Stage == LifecycleStage.Manufacturing)
                                        {
                                            <div class="lifecycle-field">
                                                <label class="form-label" for="equipment-@stageSection.Slug">設備類別（選填）</label>
                                                <select class="form-select" id="equipment-@stageSection.Slug" name="equipmentCategory" data-controlled-other data-other-target="#equipment-other-@stageSection.Slug">
                                                    <option value="">不指定設備</option>
                                                    <option value="生產機台">生產機台</option>
                                                    <option value="空壓機">空壓機</option>
                                                    <option value="鍋爐">鍋爐</option>
                                                    <option value="冰水主機／冷凍設備">冰水主機／冷凍設備</option>
                                                    <option value="空調／通風">空調／通風</option>
                                                    <option value="泵浦／馬達">泵浦／馬達</option>
                                                    <option value="照明">照明</option>
                                                    <option value="__other__">其他（自行輸入）</option>
                                                </select>
                                                <input class="form-control mt-2" id="equipment-other-@stageSection.Slug" name="equipmentCategoryOther" placeholder="請輸入其他設備類別" maxlength="200" hidden />
                                                <p class="workspace-select-help">設備類別為選填；品牌、型號、功率與效率只在 PCR 或分配佐證需要時填入情境說明。</p>
                                            </div>
                                        }
                                        <div class="lifecycle-field">
                                            <label class="form-label" for="source-type-@stageSection.Slug">資料來源類型</label>
                                            <select class="form-select" id="source-type-@stageSection.Slug" name="dataSourceType" required data-controlled-other data-other-target="#source-type-other-@stageSection.Slug">
                                                <option value="一級數據－直接量測">一級數據－直接量測</option>
                                                <option value="一級數據－帳單／發票">一級數據－帳單／發票</option>
                                                <option value="一級數據－供應商提供">一級數據－供應商提供</option>
                                                <option value="二級數據－資料庫／文獻">二級數據－資料庫／文獻</option>
                                                <option value="估算／替代資料">估算／替代資料</option>
                                                <option value="__other__">其他（自行輸入）</option>
                                            </select>
                                            <input class="form-control mt-2" id="source-type-other-@stageSection.Slug" name="dataSourceTypeOther" placeholder="請輸入其他來源類型" maxlength="200" hidden />
                                        </div>
                                        <div class="lifecycle-field">
                                            <label class="form-label" for="provider-@stageSection.Slug">資料提供者</label>
                                            <select class="form-select" id="provider-@stageSection.Slug" name="dataProviderType" required data-controlled-other data-other-target="#provider-other-@stageSection.Slug">
                                                <option value="本組織／廠場">本組織／廠場</option>
                                                <option value="供應商">供應商</option>
                                                <option value="公用事業單位">公用事業單位</option>
                                                <option value="物流／處理業者">物流／處理業者</option>
                                                <option value="主管機關／資料庫維護者">主管機關／資料庫維護者</option>
                                                <option value="__other__">其他（自行輸入）</option>
                                            </select>
                                            <input class="form-control mt-2" id="provider-other-@stageSection.Slug" name="dataProviderOther" placeholder="請輸入其他提供者" maxlength="300" hidden />
                                        </div>
                                        <div class="lifecycle-field">
                                            <label class="form-label" for="collection-@stageSection.Slug">取得方式</label>
                                            <select class="form-select" id="collection-@stageSection.Slug" name="collectionMethod" required data-controlled-other data-other-target="#collection-other-@stageSection.Slug">
                                                <option value="直接量測">直接量測</option>
                                                <option value="帳單／發票／出貨紀錄">帳單／發票／出貨紀錄</option>
                                                <option value="ERP／MES／管理系統匯出">ERP／MES／管理系統匯出</option>
                                                <option value="供應商聲明／問卷">供應商聲明／問卷</option>
                                                <option value="資料庫／文獻查詢">資料庫／文獻查詢</option>
                                                <option value="計算／估算">計算／估算</option>
                                                <option value="__other__">其他（自行輸入）</option>
                                            </select>
                                            <input class="form-control mt-2" id="collection-other-@stageSection.Slug" name="collectionMethodOther" placeholder="請輸入其他取得方式" maxlength="300" hidden />
                                        </div>
                                        <div class="lifecycle-field lifecycle-field--wide">
                                            <label class="form-label" for="source-reference-@stageSection.Slug">來源參照</label>
                                            <input class="form-control" id="source-reference-@stageSection.Slug" name="sourceReference" placeholder="帳單、電表、文件、資料集或供應商聲明編號" required maxlength="500" />
                                        </div>
'''
if old not in text:
    raise SystemExit("activity form target not found")
path.write_text(text.replace(old, new, 1))

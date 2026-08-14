# DmPayQuery — 代码逻辑总览

> 生成时间：自动

## 概览
本仓库为 DmPayQuery（WPF 客户端），目标框架 .NET 10。主要职责：从后台 API 拉取消费/流水/实名/开厅等数据，按 Excel 列逐行查询并写回，提供日志与进度反馈，最终导出结果。

项目主要模块与文件：
- ViewModels/MainViewModel.cs
  - UI 绑定与命令：SelectFile、StartQuery、CancelQuery、ClearLogs、OpenDashboard
  - 查询模式：IdRechargeOrGift / IdGiftOnly / UidRechargeOrGift / RoomSerialAndCreateTime / AnchorSerialAndIdCard
  - 时间处理：多种 DateMode/DeadlineMode，自定义开始/结束时间（小时/分/秒）
  - 并发与取消：使用 CancellationTokenSource、Concurrency（AdvancedConcurrency）以及进度分批更新（ProgressUpdateBatchSize）
  - DataTable 行读写受 _rowWriteLock 保护，使用 SafeGetColumn/SafeSetColumn 以避免缺列抛异常
  - 金额显示：整数或最多两位小数（格式化逻辑位于写入“金额”列处）

- Services/IApiService.cs
  - 声明所有外部 API 所需的异步方法签名（登录、校验 Token、充值/送礼查询、用户信息、厅/主播流水、实名与头像等）

- Services/ApiService.cs
  - HttpClient 注入，BaseUrl = "https://zapi.shanmiaobanyin.com/api"
  - 主要实现：
	- CheckTokenValidityAsync：简单 GET 验证并解析返回 code/message
	- LoginAsync / GetVerificationCodeAsync：登录与验证码（POST JSON）
	- GetRechargeAmountAsync：ID/UID 查充值或送礼
	  - 当前逻辑：调用新版路径 `/admin/system/admin/billRecordCheck/listGroup`（参数 userNumber/uid, currency=0, pageNumber=1）
	  - 解析：兼容 data.rows / rows / list / 顶层 data array
	  - 金额处理：支持 number 或 string 类型的 totalActualAmount
	  - 类型识别：优先使用 objType（1=充值，5=送礼），回退使用 objTypeDesc 文本或 sourceType
	  - 当前策略：对同类记录进行累加（rechargeSum / giftSum），再比较返回较大者；可按需切换为“覆盖”或“取最大单笔”策略
	- GetUserInfoAsync / GetUserIdCardAsync / GetUserIdCardAndAvatarAsync：
	  - 新版调用路径 `/admin/system/admin/userCheckAdmin/getlist`，使用 POST + application/json ("{}")，并兼容返回结构：data 可为数组或对象；uid 可位于 users.uid / account.uid / 顶层 uid；注册时间可来自 users.agreementSignTime（秒或毫秒）、users.createTime、account.signTime 或 loginTime
	  - 下载头像使用 HttpClient.GetByteArrayAsync
	- GetRoomSerialAsync / GetGuildCreateTimeAsync / GetAnchorSerialAsync：厅流水与开厅时间、主播流水（分页）
	  - 统一使用真实接口前缀 `https://zapi.shanmiaobanyin.com/api`，不再执行备用地址重试
	  - AnchorSerial 支持分页并累加 totalGold 或 totalGoldNum
  - 辅助函数：GetJsonElementString / GetJsonElementInt64 / GetJsonElementInt32 等，适配字符串/数字的 JSON 字段

- Models/ApiResponse.cs
  - 定义 ApiResponse<T>、BillGroupItem、RoomSerialItem、AnchorSerialItem、UserInfo 等 DTO

- 其它：
  - IExcelService、ICacheService：用于读取 Excel、缓存登录（LoginCache）
  - Views/LoginDialog：登录 UI（会返回 AccessToken）

## 主要控制流（按查询一行）
1. MainViewModel 读取 Excel 行，派发到 ProcessRowAsync
2. 根据 QueryMode 调用 ProcessConsumeRowAsync（消费模式）或 ProcessAdvancedRowAsync（高级模式）
3. ProcessConsumeRowAsync 根据模式确定查询键（消费ID / 消费UID），计算 startDate/endDate，然后：
   - 调用 apiService.GetRechargeAmountAsync 取得 (amount, bizType, error)
   - 若非 UID 模式再调用 apiService.GetUserInfoAsync 取得 UID 与注册日期
   - 将结果用 SafeSetColumn 写回 DataRow
4. ProcessAdvancedRowAsync 在模式 4/5 分别调用 GetRoomSerialAsync/GetGuildCreateTimeAsync 或 GetAnchorSerialAsync/GetUserIdCardAndAvatarAsync
5. 日志与进度通过 Logs 集合、ProgressValue/ProgressVisible 更新 UI

## 错误处理与兼容策略
- 网络/解析异常不会终止整个批次：单行异常记录 FailCount 并写入日志
- JSON 解析尽量鲁棒：兼容多种字段名与数据类型
- API 统一使用真实前缀，不再维护备用路径
- 对 POST 请求，使用 application/json 空对象避免 415

## 可配置点与扩展
- AnchorSerialPageSize 可在运行时调整，影响主播流水分页效率
- AdvancedConcurrency 控制高级模式并发数
- Publish/打包偏好已纳入 .github/copilot-instructions.md（可生成便携单文件）

## 如何定位代码
- 逻辑入口：ViewModels/MainViewModel.cs
- 外部请求实现：Services/ApiService.cs
- 数据模型：Models/ApiResponse.cs

---

如果需要我把该 Markdown 文件复制到桌面，我已在仓库生成此文件，并可立即复制到桌面。
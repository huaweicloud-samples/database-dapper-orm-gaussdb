# 测试指南

本发行版的测试位于 `tests/Dapper.Tests`（上游测试套件 + GaussDB 用例）。GaussDB
相关用例共 **44 个**，分四个文件：

| 文件 | 用例数 | 形态 |
|---|---|---|
| `GaussDBFeatureSupportTests.cs` | 1 | 离线单元（数组能力注册） |
| `GaussDBTypeHandlerTests.cs` | 29 | 离线单元（两个 TypeHandler 的解析与注册语义） |
| `Providers/GaussDBTests.cs` | 8 | 连库集成（连通性、Oracle 类型 round-trip、事务、数组参数、handler 缓存语义） |
| `RainbowGaussDBTests.cs` | 6 | 连库集成（Rainbow CRUD、主键类型边界、表名解析两条路径） |

连库用例由 `[FactGaussDB]` 门控：连接探针失败（实例不可达 / 未配置连接）时标记
**跳过**而非失败，不会误连其它实例。

## 环境要求

- **.NET 10 SDK**——构建硬要求（C# 13 / LangVersion 13 + SourceLink 11 工具链，
  SDK 8 编译不过）。
- **.NET 8 运行时**——`HuaweiCloud.GaussDB.Driver` 仅提供 net8.0 目标，GaussDB
  连库用例只存在于 net8.0（及更高）TFM。
- **GaussDB 507 集中式实例**（A 模式）——连库用例需要，本地可用 Docker 部署；
  服务端认证需 MD5（`password_encryption_type = 1`），与部署要求一致（见 GAUSSDB.md）。
- 无实例也能跑：30 个离线单元照常执行，14 个连库用例自动跳过。

## 配置连接（环境变量）

二选一：

| 环境变量 | 含义 |
|---|---|
| `GAUSSDB_PASSWORD` | 只提供密码，其余按默认拼装：`Host=localhost;Port=5432;Database=dappertest;Username=appuser` |
| `GaussDBConnectionString` | 整串覆盖连接（优先级更高） |

约定：密码**不硬编码**（源码与文档均无）；测试输出与 .trx 产物中的连接串由
`TestBase.cs` 自动掩码为 `Password=***`。

## 运行命令

```powershell
# 环境变量（PowerShell 示例）
$env:GAUSSDB_PASSWORD = "<your-password>"

# 全部 GaussDB 用例
dotnet test tests/Dapper.Tests/Dapper.Tests.csproj -f net8.0 --filter "FullyQualifiedName~GaussDB"

# 全量套件（所有 provider）
dotnet test tests/Dapper.Tests/Dapper.Tests.csproj -f net8.0
```

### 各 TFM 的实测覆盖

| TFM | GaussDB 过滤集 | 说明 |
|---|---|---|
| net8.0（带连接配置） | 44/44 通过 | 主战场：驱动仅 net8.0 |
| net8.0（未配置连接） | 30 通过 / 14 跳过 | 连库用例被探针门控跳过 |
| net10.0（带连接配置） | 44/44 通过 | 与 net8.0 覆盖相同 |
| net481 | 28/28 通过 | 仅 `GaussDBTypeHandlerTests`（DateOnly 用例被 `#if NET6_0_OR_GREATER` 排除）；FeatureSupport 与连库文件整体 `#if NET8_0_OR_GREATER` |

net481 下无连库覆盖是**固有约束**（驱动仅提供 net8.0 目标），不是遗漏。

## 预期基线与零漂移判据

全量套件在"有 GaussDB、无 SQL Server"的机器上的当前基线：

| 总数 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| 844 | 110 | 704 | 30 |

- **704 个失败全部是 SQL Server provider 用例**（`SystemSqlClient*` /
  `MicrosoftSqlClient*` 等变体），系本机无 SQL Server——上游既有行为，
  非本适配引入。
- **零漂移判据 = 失败名集合逐名比对，不是计数比对**：xunit 的测试类调度顺序不稳定
  （同一提交两次全量，总数可能 ±1），失败集合应严格一致。做法：把新产物的失败
  测试名集合与已留档的基线 .trx 做双向差集，两个方向的差集都必须为空。
- 失败集合中出现任何 GaussDB 相关用例即为回归，不得发布。

## 测试约定（写新用例前必读）

- **表命名与清理**：连库用例的物理表用 `gdb*` / `gaussdb_*` 前缀（Rainbow 用例
  沿用上游 `widget` / `gizmos` 命名）；建表前 `drop table if exists`、`finally`
  中清理——保证可重复执行、不在共享测试库里残留。
- **全局状态纪律**：handler 注册、typeMap、查询缓存都是进程级状态，测试类之间存在
  顺序依赖。触碰全局状态的用例必须**自带前提**（如 `SqlMapper.ResetTypeHandlers()` +
  前置断言）与**收尾**（如 `PurgeQueryCache()`），既不依赖、也不假设其他用例留下的状态。
- **A 模式标识符**：物理表 / 列名一律小写书写（未加引号的标识符折叠为小写），
  见 GAUSSDB.md §11。
- **异常断言**：驱动处于 preview 态，凡"必须响铃失败"类契约优先断言行为
  （如 `Assert.ThrowsAny` + 注释记录实测类型），紧绑驱动内部异常类型前先看既有
  用例的处理方式。

## GaussDB 版本升级时的回归流程（维护者）

维护触发条件 = **GaussDB 版本更新**（Dapper 上游不跟进）。升级后按序：

1. `dotnet build Dapper.slnx`，构建零错误。
2. 跑全部 44 个 GaussDB 用例，应全绿。
3. 跑全量套件，按"失败名逐名比对"确认零漂移。
4. 复跑探针脚本复核关键兼容性结论。探针资产与定档记录在仓库工作区的 `probe/`
   目录（`verification-results.md` 是全部兼容性结论的原始依据；该目录不入库）。
5. 评估漂移：结论有变化的，更新 GAUSSDB.md 与测试；无变化的，递增 tag 后缀
   （`v2.1.89-gaussdb.N`）出发行版。

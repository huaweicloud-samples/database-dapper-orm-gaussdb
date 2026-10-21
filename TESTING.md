# 测试指南

本发行版的测试位于 `tests/Dapper.Tests`（上游测试套件 + GaussDB 用例）。GaussDB
相关用例共 **44 个**，分布于以下四个文件：

| 文件 | 用例数 | 类型 |
|---|---|---|
| `GaussDBFeatureSupportTests.cs` | 1 | 离线单元（数组能力注册） |
| `GaussDBTypeHandlerTests.cs` | 29 | 离线单元（两个 TypeHandler 的解析与注册语义） |
| `Providers/GaussDBTests.cs` | 8 | 连库集成（连通性、Oracle 类型往返、事务、数组参数、handler 缓存语义） |
| `RainbowGaussDBTests.cs` | 6 | 连库集成（Rainbow CRUD、主键类型边界、表名解析两条路径） |

连库用例由 `[FactGaussDB]` 特性门控：当连接探针失败（实例不可达 / 未配置连接）时，
用例标记为**跳过**而非失败，避免误连其他实例。

## 环境要求

- **.NET 10 SDK**——构建的必要条件（C# 13 / LangVersion 13 + SourceLink 11 工具链，
  .NET 8 SDK 无法完成编译）。
- **.NET 8 运行时**——`HuaweiCloud.GaussDB.Driver` 仅提供 net8.0 目标，GaussDB 连库
  用例仅存在于 net8.0 及更高 TFM。
- **GaussDB 507 集中式实例**（A 兼容模式）——连库用例所需，本地环境可通过 Docker
  部署；服务端须启用 MD5 认证（`password_encryption_type = 1`），与部署要求一致
  （见 GAUSSDB.md）。
- 未部署实例的环境下：30 个离线单元用例可正常执行，14 个连库用例自动跳过。

## 连接配置（环境变量）

以下两种方式任选其一：

| 环境变量 | 说明 |
|---|---|
| `GAUSSDB_PASSWORD` | 仅提供密码，其余按默认值拼装：`Host=localhost;Port=5432;Database=dappertest;Username=appuser` |
| `GaussDBConnectionString` | 整串覆盖连接字符串（优先级更高） |

约定：密码**不硬编码**（源码与文档均不包含）；测试输出及 .trx 产物中的连接串由
`TestBase.cs` 自动掩码为 `Password=***`。

## 运行命令

```powershell
# 设置环境变量（PowerShell 示例）
$env:GAUSSDB_PASSWORD = "<your-password>"

# 执行全部 GaussDB 用例
dotnet test tests/Dapper.Tests/Dapper.Tests.csproj -f net8.0 --filter "FullyQualifiedName~GaussDB"

# 执行全量测试套件（覆盖所有 provider）
dotnet test tests/Dapper.Tests/Dapper.Tests.csproj -f net8.0
```

### 各目标框架的覆盖范围

下表数值均为实测结果：

| TFM | GaussDB 过滤集 | 说明 |
|---|---|---|
| net8.0（已配置连接） | 44/44 通过 | 主要验证环境：驱动仅提供 net8.0 |
| net8.0（未配置连接） | 30 通过 / 14 跳过 | 连库用例经探针门控跳过 |
| net10.0（已配置连接） | 44/44 通过 | 覆盖范围与 net8.0 相同 |
| net481 | 28/28 通过 | 仅 `GaussDBTypeHandlerTests`（DateOnly 用例被 `#if NET6_0_OR_GREATER` 排除）；FeatureSupport 与连库文件整体位于 `#if NET8_0_OR_GREATER` 内 |

net481 下无连库覆盖属**固有约束**（驱动仅提供 net8.0 目标框架），并非遗漏。

## 预期基线与零漂移判定标准

全量测试套件在"已部署 GaussDB、未部署 SQL Server"环境下的当前基线：

| 总数 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| 844 | 110 | 704 | 30 |

- **704 个失败全部为 SQL Server provider 用例**（`SystemSqlClient*` /
  `MicrosoftSqlClient*` 等变体），原因为本机未部署 SQL Server——属上游既有行为，
  与本适配无关。
- **零漂移判定标准 = 失败用例名称集合逐名比对，而非计数比对**：xunit 的测试类调度
  顺序不稳定（同一提交两次全量执行，总数可能相差 ±1），失败集合应严格一致。具体
  做法：将新产物的失败用例名集合与已留档的基线 .trx 做双向差集，两个方向的差集
  均须为空。
- 失败集合中出现任何 GaussDB 相关用例即构成回归，不得发布。

## 测试约定（新增用例须遵循）

- **表命名与清理**：连库用例的物理表使用 `gdb*` / `gaussdb_*` 前缀（Rainbow 用例
  沿用上游 `widget` / `gizmos` 命名）；建表前执行 `drop table if exists`，并在
  `finally` 块中完成清理——保证用例可重复执行，不在共享测试库中残留对象。
- **全局状态纪律**：handler 注册、typeMap、查询缓存均为进程级状态，测试类之间存在
  顺序依赖。涉及全局状态的用例须**自建前提**（如 `SqlMapper.ResetTypeHandlers()` 及
  前置断言）并**完成收尾**（如 `PurgeQueryCache()`），既不依赖、也不假设其他用例
  遗留的状态。
- **A 兼容模式标识符**：物理表名 / 列名一律以小写书写（未加引号的标识符折叠为小写），
  见 GAUSSDB.md §11。
- **异常断言**：驱动处于 preview 阶段，凡"必须显式失败"类契约，优先断言行为本身
  （如 `Assert.ThrowsAny` 并以注释记录实测异常类型）；需绑定驱动内部异常类型时，
  参照既有用例的处理方式。

## GaussDB 版本升级的回归流程（维护者）

维护触发条件为 **GaussDB 版本更新**（不跟随 Dapper 上游更新）。升级后按以下顺序执行：

1. 执行 `dotnet build Dapper.slnx`，确认构建零错误。
2. 执行全部 44 个 GaussDB 用例，应全部通过。
3. 执行全量测试套件，按"失败用例逐名比对"确认零漂移。
4. 复跑探针脚本，复核关键兼容性结论。探针资产与定档记录位于仓库工作区的 `probe/`
   目录（`verification-results.md` 为全部兼容性结论的原始依据；该目录不入库）。
5. 评估漂移情况：结论发生变化的，更新 GAUSSDB.md 及相应测试；结论无变化的，递增
   标签后缀（`v2.1.89-gaussdb.N`）发布版本。

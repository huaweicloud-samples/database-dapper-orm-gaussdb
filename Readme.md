# Dapper GaussDB 适配版本说明

本发行版基于 [Dapper](https://github.com/DapperLib/Dapper) 2.1.89 源码构建，为华为
**GaussDB 507 集中式（Oracle 兼容模式，`DBCOMPATIBILITY = 'A'`）** 提供 Dapper 层适配；
在未启用适配功能时，Dapper 在其他数据库提供程序下的行为与上游版本保持一致。文档所载
兼容性结论均经真实环境验证。

## 上游官方文档

Dapper 框架自身的使用方式（`Query` / `Execute` / 多对象映射 / 存储过程调用等）请参阅
上游官方文档，本说明不再重复：

- 官方代码仓库：<https://github.com/DapperLib/Dapper>
- 官方文档（Wiki）：<https://github.com/DapperLib/Dapper/wiki>
- NuGet 包：<https://www.nuget.org/packages/Dapper>
- 版本发布记录：<https://github.com/DapperLib/Dapper/releases>

## 适配内容

本发行版配套官方 .NET 驱动 `HuaweiCloud.GaussDB.Driver` 8.0.1（基于 Npgsql 8.0，
仅提供 net8.0 目标框架），主要变更如下：

| 变更项 | 说明 |
|---|---|
| `FeatureSupport` 注册 GaussDB | 声明数组参数能力；列表查询（in-list）须使用 `= any(@ids)` 形式，Dapper 依此走数组参数绑定路径 |
| `GaussDBDateHandler` | 支持 `date::text`（`17-OCT-26` 形态）至 `DateTime` 的转换；含 BC 纪元防护与 `DateOnly` 兼容处理 |
| `GaussDBHexByteArrayHandler` | 支持 `raw` / `blob` 列 `::text` 十六进制文本与 `byte[]` 的相互转换 |
| `GaussDBSetup.Initialize()` | 统一注册入口，进程级幂等 |
| `Dapper.Rainbow` GaussDB 方言 | `Insert` 采用 `returning Id`、`First` 采用 `limit 1`；表名解析与回退遵循 A 兼容模式标识符规则 |
| 测试基础设施 | 44 个 GaussDB 测试用例（离线单元 30 + 连库集成 14）、连接探针自动跳过机制、连接串密码掩码 |

- **兼容性差异与使用约束**：[GAUSSDB.md](GAUSSDB.md) —— 症状速查表及 13 项经真实
  环境验证的约束说明
- **完整变更清单**：见 [GAUSSDB.md](GAUSSDB.md) 改动清单一节；相对基线的完整差异可
  执行 `git diff d7ee1ee HEAD -- Dapper-2.1.89/` 查看

**上游兼容性声明**：应用程序若未调用 `GaussDBSetup.Initialize()`，Dapper 的行为与
上游 2.1.89 保持一致（已通过本版本与基线的全量测试对照及逐项探针验证）；未使用
GaussDB 的存量项目升级至本版本无需任何代码变更。

## 部署指南

1. 安装 `HuaweiCloud.GaussDB.Driver`（官方 .NET 驱动，仅提供 **net8.0** 目标框架，
   .NET Framework 应用不适用）。
2. 在应用启动阶段调用一次 `Dapper.GaussDBSetup.Initialize()`（进程级注册、幂等；须
   先于首条查询执行，注册语义的注意事项见 GAUSSDB.md 快速开始）。
3. 完成上述步骤后即可使用标准 Dapper API。读取 Oracle 私有类型列（`date` / `raw` /
   `blob` / `clob`）时，须在 SQL 中显式转换（`::timestamp` / `::text`），对应关系见
   GAUSSDB.md §1。
4. 服务端要求：GaussDB A 兼容模式，并启用 MD5 认证（`password_encryption_type = 1`，
   `gs_hba.conf` 相应放行），否则驱动无法建立连接。

部署细节、全部使用约束及按错误码检索的速查表，见 [GAUSSDB.md](GAUSSDB.md)。

## 开发指南

- **构建**：`dotnet build Dapper.slnx`。需安装 .NET 10 SDK（C# 13 / SourceLink 11
  工具链，.NET 8 SDK 无法完成编译）；本地仓库未配置远程时出现 SourceLink 警告属
  预期现象。
- **分支模型**：`master` 分支保存上游 2.1.89 原始基线（提交 `d7ee1ee`，不做改动）；
  `gaussdb` 分支为适配开发分支（当前发行版本，标签 `v2.1.89-gaussdb.1`）。
- **核心变更位置**：`Dapper/GaussDB*.cs`（三个新增文件）及 `Dapper/FeatureSupport.cs`；
  `Dapper.Rainbow/Database.cs` / `Database.Async.cs`（方言分支）；其余见 GAUSSDB.md
  改动清单。
- **变更约束**（修改代码前须了解）：
  - TypeHandler 仅依赖 `System.Data`，不引用驱动类型，以保持核心库的多 TFM 多目标；
  - 新增公共 API 须同步登记至 `Dapper/PublicAPI.Unshipped.txt`；
  - 兼容性结论发生变化时，须同步更新 GAUSSDB.md（该文档各条目均以真实环境验证为依据）。

## 测试指南

测试环境配置、运行命令、各目标框架的覆盖范围、全量测试基线与零漂移判定标准，以及
GaussDB 版本升级时的回归流程，见 [TESTING.md](TESTING.md)。

## 许可证

- 本项目与上游一致，采用 Apache License 2.0（见 [License.txt](License.txt)）。
- Dapper 原始版权归 DapperLib 所有；本发行版的修改文件清单见 GAUSSDB.md 改动清单
  （对应 Apache 2.0 第 4(b) 条的修改声明要求）。

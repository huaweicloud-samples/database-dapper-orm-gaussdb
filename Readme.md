# Dapper GaussDB 适配版

本仓库是 [Dapper](https://github.com/DapperLib/Dapper) 2.1.89 源码的 GaussDB 适配版：
在不动核心行为的前提下，为华为 **GaussDB 507 集中式（Oracle 兼容 A 模式，
`DBCOMPATIBILITY = 'A'`）** 补齐 Dapper 层的适配。全部兼容性结论经真库实测定档。

## 上游文档

Dapper 本身的用法（`Query` / `Execute` / 多映射 / 存储过程等）本文不再复述，直接看官方：

- 官方仓库：<https://github.com/DapperLib/Dapper>
- 官方文档（Wiki）：<https://github.com/DapperLib/Dapper/wiki>
- NuGet：<https://www.nuget.org/packages/Dapper>
- Release Notes：<https://github.com/DapperLib/Dapper/releases>

## 本发行版做了什么

配套官方 .NET 驱动 `HuaweiCloud.GaussDB.Driver` 8.0.1（Npgsql 8.0 fork，仅 net8.0）：

| 改动 | 说明 |
|---|---|
| `FeatureSupport` 注册 GaussDB | 数组能力识别；in-list 参数必须写 `= any(@ids)`（Dapper 据此走数组参数路径） |
| `GaussDBDateHandler` | `date::text`（`17-OCT-26` 形态）→ `DateTime`；BC 纪元守卫、DateOnly 容错 |
| `GaussDBHexByteArrayHandler` | `raw` / `blob` 的 `::text` hex 文本 ↔ `byte[]` |
| `GaussDBSetup.Initialize()` | 一行注册入口（进程级、幂等） |
| `Dapper.Rainbow` GaussDB 方言 | `Insert` 用 `returning Id`、`First` 用 `limit 1`；表名探测/回退按 A 模式标识符规则 |
| 测试基建 | 44 个 GaussDB 用例（离线单元 30 + 连库集成 14）、连接探针自动跳过、连接串密码掩码 |

- **兼容性差异与使用约束**：[GAUSSDB.md](GAUSSDB.md) —— 症状速查表 + 13 条真库实测定档的约束
- **完整改动清单**：见 [GAUSSDB.md](GAUSSDB.md) 改动清单一节（对照基线的完整差异：
  `git diff d7ee1ee HEAD -- Dapper-2.1.89/`）

**上游兼容性承诺**：不调用 `GaussDBSetup.Initialize()` 时，Dapper 行为与上游 2.1.89
逐字一致（已由 HEAD 与基线的全量测试对照及逐项探针验证）；不使用 GaussDB 的项目
升级到本版本无感知。

## 如何部署

面向使用本发行版的应用：

1. 安装 `HuaweiCloud.GaussDB.Driver`（官方驱动，仅 **net8.0** 目标——.NET Framework
   应用不可用）。
2. 应用启动时调用一次 `Dapper.GaussDBSetup.Initialize()`（进程级、幂等；必须早于第一条
   查询——注册语义的四个注意点见 GAUSSDB.md 快速开始）。
3. 之后即用标准 Dapper API。Oracle 私有类型列（`date` / `raw` / `blob` / `clob`）读取时
   需要显式 cast（`::timestamp` / `::text`），对照表见 GAUSSDB.md §1。
4. 服务端要求：GaussDB A 模式 + MD5 认证（`password_encryption_type = 1` +
   `gs_hba.conf` 放行），否则驱动无法直连。

部署细节、全部约束与按错误码反查的速查表：[GAUSSDB.md](GAUSSDB.md)。

## 如何开发

- **构建**：`dotnet build Dapper.slnx`（需 .NET 10 SDK——C# 13 / SourceLink 11 工具链，
  SDK 8 编译不过；本地 git 无远程时 SourceLink 警告属预期）。
- **分支形态**：`master` = 上游 2.1.89 原始基线（提交 `d7ee1ee`，保持不动）；
  `gaussdb` = 适配工作分支（当前发行版，tag `v2.1.89-gaussdb.1`）。
- **核心改动位置**：`Dapper/GaussDB*.cs`（三个新文件）与 `Dapper/FeatureSupport.cs`；
  `Dapper.Rainbow/Database.cs` / `Database.Async.cs`（方言分支）；其余见 GAUSSDB.md
  改动清单。
- **改动约束**（动代码前必读）：
  - handler 只依赖 `System.Data`，不引用驱动类型——保住核心库的多 TFM 多目标；
  - 新增 public API 需同步登记 `Dapper/PublicAPI.Unshipped.txt`；
  - 兼容性结论变化时同步更新 GAUSSDB.md（该文档全部条目以真库实测为依据）。

## 如何测试

见 [TESTING.md](TESTING.md)：环境变量配置、运行命令、44 个用例的分布与
各 TFM 覆盖、全量基线的零漂移判据、GaussDB 版本升级时的回归流程。

## 许可证

- 本项目与上游一致采用 Apache License 2.0（见 [License.txt](License.txt)）。
- Dapper 原版权归 DapperLib 所有；本发行版修改过的文件清单见 GAUSSDB.md 改动清单
  （Apache 2.0 §4(b) 的修改标注）。

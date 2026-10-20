# Dapper GaussDB 适配说明

本发行版基于 Dapper 2.1.89 改造，增加华为 GaussDB 507 集中式 / Oracle 兼容模式
（`DBCOMPATIBILITY = 'A'`）支持。全部结论经真库实测定档，未经验证的路径不在本文档范围内。

**读者对象**：使用本发行版的应用开发者；标注（维护者）的条目面向仓库维护者。

## 症状速查

按屏幕上看到的错误码 / 异常反查对应约束：

| 症状（错误码 / 异常） | 节号 |
|---|---|
| `InvalidCastException` + `DataTypeName 'datea'` / `'raw'` / `'blob'` / `'clob'` | §1 列必须显式 cast |
| `InvalidCastException` + `Out of range of DateTime` | §2 BC 日期限制 |
| `42804` + `raw but expression is of type bytea` | §4 `raw` 写入需 `hextoraw` |
| `42601` + `"$1"` | §8 in-list 用 `= any(@ids)` |
| `42601` + `subprogram body is not ended correctly` | §9 PL/SQL 复合语句 |
| `42P01`（`Relation "xxx" does not exist`） | §11 标识符大小写 |
| `Parameter 'v' ... out-only parameter` | §12 存储过程输出参数 |
| `DataException`（内层 `OverflowException`，Rainbow `Insert` 取回主键） | §11 · Rainbow 边界 |

## 快速开始

```csharp
using Dapper;

// 应用启动时调用一次（进程级注册，幂等）
Dapper.GaussDBSetup.Initialize();

using var conn = new HuaweiCloud.GaussDB.GaussDBConnection(
    "Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<pwd>");
var rows = conn.Query<Order>("select * from orders where id = @id", new { id = 1 });
```

需要 `HuaweiCloud.GaussDB.Driver`（官方 .NET 驱动，Npgsql 8.0 fork，仅 net8.0）。

关于注册语义，有四点必须注意：

- `GaussDBSetup.Initialize()` 是**进程级全局注册**，幂等，可多线程重复调用。
- 注册必须**早于任何查询**。`SqlMapper.AddTypeHandler` 不会清空 Dapper 的查询缓存，
  SQL 文本已被缓存的查询会继续使用此前解析出的反序列化器。若无法保证注册时机，
  在 `Initialize()` 之后调用 `SqlMapper.PurgeQueryCache()` 强制重新解析。
- `Initialize()` 是**保守注册**：对**已有第三方 handler 的类型不覆盖**。若进程内其它组件
  已经注册了 `DateTime` / `byte[]` handler，GaussDB 的对应 handler 不会生效
  （`c_date::text` / `raw::text` 通道随之失效），需要自行调整注册顺序，或在 `Initialize()`
  之前先 `SqlMapper.ResetTypeHandlers()` 清掉旧注册。
- handler **只在读侧生效**。Dapper 的写入路径（`SqlMapper.LookupDbType`）先从内建类型表
  解析 `DateTime` / `byte[]`，不会走到 handler，写参数由驱动与服务端直接接收；
  handler 的 `SetValue` 仅为满足接口契约而存在。

## 兼容性差异与使用约束

### 1. `date` / `raw` / `blob` / `clob` 列必须显式 cast

**现象**：直读这些列抛
`InvalidCastException: Reading as 'System.Object' is not supported for fields having DataTypeName 'datea'`
（`raw` / `blob` / `clob` 报对应的类型名）。标量查询与 POCO 成员映射路径行为一致。

**原因**：A 模式把这四者暴露为驱动未映射的私有类型（`datea` / `raw` / `blob` / `clob`）。
驱动 `GetValue` 直接抛异常，Dapper 的 TypeHandler 无从介入（handler 拿不到值），
这是驱动层限制，Dapper 无法绕过。

**正确写法**：查询时显式 cast。

| 列类型 | 写法 | 映射目标 |
|---|---|---|
| `date` | `select c_date::timestamp` | `DateTime`（Dapper 原生，无需 handler） |
| `date` | `select c_date::text` | `DateTime`（经 `GaussDBDateHandler`） |
| `raw` | `select c_raw::text` | `byte[]`（经 `GaussDBHexByteArrayHandler`） |
| `blob` | `select c_blob::text` | `byte[]`（经 `GaussDBHexByteArrayHandler`） |
| `clob` | `select c_clob::text` | `string` |

```sql
select c_date::timestamp as CDate,
       c_raw::text       as CRaw,
       c_blob::text      as CBlob,
       c_clob::text      as CClob
from t where id = @id
```

> 新建表建议用 `timestamp` 而非 `date`，可避免 cast。

### 2. BC 日期限制

**现象**：两条通道都不支持 BC 日期，且其中一条是**静默数据损坏**：

- `date::text` 输出 `15-MAR-44`，**没有纪元后缀**——纪元在服务端格式化时已经丢失。
  `GaussDBDateHandler` 只能按 Oracle RR 规则解析，把 `44` 映射为 **AD 2044**，不抛异常。
- `date::timestamp` 保留纪元（`0044-03-15 00:00:00 BC`），但 .NET `DateTime` 无法表示 BC
  （`MinValue` 为 0001-01-01），驱动直读抛
  `InvalidCastException: Out of range of DateTime (year must be between 1 and 9999).`

**原因**：`date::text` 通道的信息损失发生在服务端，handler 侧无法补救；
而 .NET 类型系统本身没有 BC 的表示能力。

**正确写法**：

- **禁止** BC 日期走 `date::text` + handler 通道（静默错值，比异常更危险）。
- BC 场景若必须走 cast，用 `date::timestamp`：它会显式抛异常，不会给出错误值。
- 业务确需 BC 日期时，`DateTime` 无法承载，应改为字符串列或在应用层自行处理——
  适配层不提供该能力。

### 3. `date` handler 的 DateStyle 假设

**现象**：`GaussDBDateHandler` 用 `TryParseExact` 匹配一组显式格式：Oracle 形态
`DD-MON-RR`（如 `17-OCT-26`，即 `date::text` 的输出）与 `datea` 私有类型自身文本表示的
ISO 形态 `2026-10-17 00:00:00`（带 `AD` 后缀，剥掉后缀后命中 `TextFormats[0]`）都命中这条
主通道；`AD` 纪元后缀可解析、`BC` 后缀**主动拒绝**，月份大小写不敏感；全部格式均不匹配时
才兜底 `DateTime.Parse`。

**原因**：`::text` 的 `DD-MON-RR` 拼写由服务端格式化产生，解析结果依赖服务端 DateStyle
的输出默认设定。

**正确写法**：**不要修改服务端 DateStyle**。改动会改变 `::text` 输出形态，
兜底解析不保证覆盖非默认形态。BC 后缀文本会被 handler 主动抛
`NotSupportedException` 拒绝（防静默误读），这是有意行为。

### 4. `raw` 列写入需 `hextoraw`

**现象**：向 `raw` 列传 `byte[]` 参数报服务端 `42804`
（`column is of type raw but expression is of type bytea`）。

**原因**：A 模式 `raw` 列拒收 `bytea` 参数，与 `blob` 列不同。

**正确写法**：`raw` 列传 hex 文本并包一层 `hextoraw`；`blob` 列可直接传 `byte[]`。

```csharp
conn.Execute("insert into t (c_raw) values (hextoraw(@raw))", new { raw = "0102ABCD" });
conn.Execute("insert into t (c_blob) values (@blob)", new { blob = new byte[] { 0x00, 0xFF } });
```

### 5. 空字符串按 NULL 处理

**现象**：写入 `''`，回读 `is null` 为 true。

**原因**：A 模式服务端行为，与 Oracle 语义一致。

**正确写法**：不要把空串当作有效值参与判空逻辑；需要区分"空串"与"NULL"时，
业务上改用哨兵值或额外标志位。

### 6. `varchar2(n)` 长度按 UTF-8 字节计

**现象**：`varchar2(10)` 只能存 3 个中文字符（9 字节），超出即硬报错
`22001: value too long for type character varying(10)`。

**原因**：`varchar2` 的长度 `n` 是**字节**语义（错误消息虽报 character varying，
实际按字节计数后拒绝，无截断）。

**正确写法**：按字节预算长度，中文按 3 字节估算。

### 7. 无 `ON CONFLICT`，用 `ON DUPLICATE KEY UPDATE`

**现象**：`insert ... on conflict ...` 报语法错误（该 PG 子句在 GaussDB 上不存在）。

**原因**：A 模式不提供 PG 的 upsert 语法，提供 Oracle 风格的 `on duplicate key update`。

**正确写法**：

```sql
-- 不支持：insert ... on conflict do nothing
insert into t (id, v) values (1, 'x') on duplicate key update v = values(v);
```

### 8. in-list 数组参数必须写 `= any(@ids)`

**现象**：`where id in @ids` 报服务端 `42601`
（`syntax error at or near "$1"`，位置指向被改写后的 `$1`）。

**原因**：GaussDB 被判为具备数组能力（`FeatureSupport.Arrays = true`），Dapper 走数组分支
（`SqlMapper.PackListParameters`）把整个数组绑定为**单个**参数，且**不改写 SQL**
（非数组分支才会展开为 `(…,…)`）；驱动把 `@ids` 改写为 `$1` 后，SQL 语法即不成立。

**正确写法**：in-list 一律用 `= any(@ids)`（与 Npgsql 生态一致）。

```csharp
// 错误：in @ids
conn.Query<T>("select * from t where id in @ids", new { ids });

// 正确：= any(@ids)
conn.Query<T>("select * from t where id = any(@ids)", new { ids });
```

### 9. PL/SQL 复合语句不要经驱动执行

**现象**：`create procedure ... begin ... end;` 这类含体内分号的语句报 `42601`
（`subprogram body is not ended correctly`，位置指向体内第一个分号）。

**原因**：驱动走扩展协议（PBE），GaussDB 服务端在 Parse 阶段按分号截断语句；
simple query 通道（`gsql` / psycopg2 / 迁移工具）同语句可成功。尾拼第二条语句
并不能触发驱动的多语句 simple 路径，workaround 无效。

**正确写法**：DDL 用 `gsql` / 迁移工具链执行；应用侧仅做 DML 与过程调用
（`call proc()` 或 `CommandType.StoredProcedure` 均可）。

### 10. 无 uuid 生成函数

**现象**：`gen_random_uuid()` / `sys_guid()` / `uuid_generate_v4()` 均报函数不存在。

**原因**：A 模式未提供任何 uuid 生成函数。

**正确写法**：`Guid` 主键在应用侧用 `Guid.NewGuid()` 生成后插入。

### 11. 标识符大小写

**现象**：未加引号创建的 `MixedCaseT` 在 `information_schema` 中存为 `mixedcaset`；
按 `'MixedCaseT'` 查询 0 行。

**原因**：未加引号的标识符折叠为**小写**（PostgreSQL 语义，非 Oracle 大写）。

**正确写法**：DDL 不加引号，或一律小写书写；查询 `information_schema` 时用小写比较。

#### Rainbow（`Database<TDatabase>`）的边界

- **物理表名、列名必须小写**（即用未加引号的 DDL 建表）。带引号的 PascalCase 表
  **无法被 Rainbow 寻址**——未引用的引用会被折叠为小写，失败表现为
  `Relation "xxx" does not exist`（SQLSTATE `42P01`），容易被误判为"表不存在"。
- 主键列同理：实体需有 `Id` 属性，GaussDB 侧列名须为小写 `id`。
- 表属性名解析：先探测 `information_schema`（**大小写不敏感**），未命中则回退为
  **裸类型名且不加引号**（加引号会破坏服务端的折叠行为）。
- `Insert` 用 `returning Id` 取回主键，内部按 `Query<int?>` 取值（经 `Convert.ChangeType`
  转 `int`，非 IL unbox）：`int4` / `serial` 主键直接可用；`int8`（`bigserial`）/
  `number` 主键在 int32 范围内同样可用，主键值超出 int32 范围时抛
  `System.Data.DataException`（内层 `System.OverflowException`）。这个边界只取决于
  `Insert` 内部的返回键解析——**即使实体 `Id` 声明为 `long` 也一样**；读侧
  `Table<T, long>.Get` 则可以用 `long` 承接超 int32 的存量主键。

### 12. 存储过程输出参数

**现象**：`call` + `DynamicParameters` 的 `Output` 参数报
`Parameter 'v' referenced in SQL but is out-only parameter`。

**原因**：驱动客户端参数检查层拒绝（非服务端错误），OUT 参数需专项研究
（函数式调用 / `InputOutput` 变体）。

**正确写法**：无参过程可用（`CommandType.StoredProcedure` 或 `call`）；
需要输出值时改用函数返回结果集。

### 13. 列名映射与下划线

**现象**：`c_number` 列映射不到 `CNumber` 属性（属性保持默认值，不报错）。

**原因**：`DefaultTypeMap.MatchNamesWithUnderscores` 默认为 false，只做大小写不敏感的
**精确**匹配，不剥离下划线。

**正确写法**：查询时给显式别名。

```sql
select c_number as CNumber, c_date::timestamp as CDate from t
```

## 部署要求

- 服务端认证需 `password_encryption_type = 1`（MD5）+ `gs_hba.conf` 允许 md5，
  PG 协议驱动方可直连。
- 驱动 `HuaweiCloud.GaussDB.Driver` 仅提供 **net8.0** 目标；.NET Framework 应用无法使用。
- 因此 **net481 下 GaussDB 分支无测试覆盖**——驱动仅 net8.0，这是固有约束，不是遗漏。
- （维护者）测试密码经 `GAUSSDB_PASSWORD` 环境变量注入（不硬编码）；也可用
  `GaussDBConnectionString` 环境变量整体覆盖连接串。未提供密码时连接探针失败，
  测试被标记为跳过而非误连其它实例。
- （维护者）构建本发行版源码需 .NET 10 SDK（C# 13 / LangVersion 13 + SourceLink 11 工具链）。

## 改动清单

相对上游 Dapper 2.1.89 的改动（维护者；完整差异：`git diff d7ee1ee HEAD -- Dapper-2.1.89/`）：

| 文件 | 改动 |
|---|---|
| `Dapper/FeatureSupport.cs` | 注册 `gaussdbconnection` 为数组能力提供者 |
| `Dapper/GaussDBDateHandler.cs` | 新增（`datea` 文本 ↔ `DateTime`，含 BC 守卫与 DateOnly 容错） |
| `Dapper/GaussDBHexByteArrayHandler.cs` | 新增（`raw` / `blob` hex ↔ `byte[]`） |
| `Dapper/GaussDBSetup.cs` | 新增（handler 注册入口，进程级幂等） |
| `Dapper/PublicAPI.Unshipped.txt` | 登记上述 public API |
| `Dapper.Rainbow/Database.cs` | GaussDB 方言分支（`IsGaussDB` 探测、`Insert` 用 `returning Id`、`First` 用 `limit 1`、表名探测与回退、小写标识符 remarks） |
| `Dapper.Rainbow/Database.Async.cs` | Async 版同构分支（`InsertAsync` / `FirstAsync`） |
| `Directory.Packages.props` | 新增 `HuaweiCloud.GaussDB.Driver` 8.0.1 版本条目 |
| `tests/Dapper.Tests/Dapper.Tests.csproj` | 新增 `Dapper.Rainbow` ProjectReference 与非 net481 的 GaussDB.Driver 包引用 |
| `tests/Dapper.Tests/TestBase.cs` | 连接串输出前掩码密码（防泄漏到测试输出与 .trx 产物） |
| `tests/Dapper.Tests/TypeHandlerTests.cs` | 修正 `finally` 语句顺序（SQL Server 不可达时不再跳过类型表还原） |
| `tests/Dapper.Tests/GaussDBFeatureSupportTests.cs` | 新增（数组能力断言） |
| `tests/Dapper.Tests/GaussDBTypeHandlerTests.cs` | 新增（两 handler 的单元测试） |
| `tests/Dapper.Tests/Providers/GaussDBTests.cs` | 新增（真库集成测试 + `FactGaussDB` 跳过守卫） |
| `tests/Dapper.Tests/RainbowGaussDBTests.cs` | 新增（Rainbow CRUD 与表名探测两路径） |
| `README.md` | 重写为本发行版说明（上游文档链接 + 本次改动、部署 / 开发 / 测试指引） |
| `TESTING.md` | 新增（测试环境与连接配置、各 TFM 覆盖、全量基线与零漂移判据、升级回归流程） |

-- 规范库测试数据库重置脚本：达梦 DM
-- 作用：删除并重建前清空规范库相关表对象。
-- 安全边界：只处理 CAD_SW_LIBRARY 下明确列出的 STANDARD_* 对象。
-- 本脚本不会删除 CAD_FILE_STORAGE、CAD_BLOCK_ATTRIBUTES_JSON、用户表或其他业务表。
-- 执行前请确认 appsettings.json 的 Database:Type=DM，且当前连接确实是测试库。
-- 执行顺序：先执行本文件，再依次执行 standard_schema_dm.sql、standard_management_schema_dm.sql、standard_template_schema_dm.sql。
-- 不要再执行 upgrade_dynamic_import_dm.sql，因为 standard_template_schema_dm.sql 已包含升级后的字段。
-- 不要执行 merge_reset_standard_series_dm.sql，因为该文件会创建临时归并表，不属于最终基础结构。

-- ============================================================
-- 一、执行前盘点：确认当前 schema 中有哪些规范对象。
-- ============================================================
SELECT OWNER, TABLE_NAME
FROM ALL_TABLES
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND TABLE_NAME IN (
	'STANDARD_FAMILIES',
	'STANDARD_SERIES',
	'STANDARD_FLANGE_RECORDS',
	'STANDARD_CATEGORIES',
	'STANDARD_DOCUMENT_VERSIONS',
	'STANDARD_DOCUMENT_FILES',
	'STANDARD_OPERATION_LOGS',
	'STANDARD_TEMPLATES',
	'STANDARD_TEMPLATE_COLUMNS',
	'STANDARD_IMPORT_BATCHES',
	'STANDARD_IMPORT_ROWS',
	'STANDARD_DYNAMIC_VERSION_ROWS'
  )
ORDER BY TABLE_NAME;

-- ============================================================
-- 二、删除动态数据行，先解除对版本和批次的外键依赖。
-- ============================================================
DROP TABLE CAD_SW_LIBRARY.STANDARD_DYNAMIC_VERSION_ROWS;
DROP TABLE CAD_SW_LIBRARY.STANDARD_IMPORT_ROWS;

-- ============================================================
-- 三、删除附件和导入批次，解除对版本、系列、模板的外键依赖。
-- ============================================================
DROP TABLE CAD_SW_LIBRARY.STANDARD_DOCUMENT_FILES;
DROP TABLE CAD_SW_LIBRARY.STANDARD_IMPORT_BATCHES;

-- ============================================================
-- 四、删除规范版本和模板字段，解除对系列、模板的外键依赖。
-- ============================================================
DROP TABLE CAD_SW_LIBRARY.STANDARD_DOCUMENT_VERSIONS;
DROP TABLE CAD_SW_LIBRARY.STANDARD_TEMPLATE_COLUMNS;

-- ============================================================
-- 五、删除规范数据、操作日志和模板主表。
-- ============================================================
DROP TABLE CAD_SW_LIBRARY.STANDARD_FLANGE_RECORDS;
DROP TABLE CAD_SW_LIBRARY.STANDARD_OPERATION_LOGS;
DROP TABLE CAD_SW_LIBRARY.STANDARD_TEMPLATES;

-- ============================================================
-- 六、删除目录、规范系列和规范大类。
-- ============================================================
DROP TABLE CAD_SW_LIBRARY.STANDARD_CATEGORIES;
DROP TABLE CAD_SW_LIBRARY.STANDARD_SERIES;
DROP TABLE CAD_SW_LIBRARY.STANDARD_FAMILIES;

-- ============================================================
-- 七、检查旧归并脚本可能遗留的临时表。
-- 这些表不是规范业务表，先只查询，不让不存在的临时表中断正式清理。
-- 如果查询到记录，再单独执行对应的 DROP TABLE 语句。
-- ============================================================
SELECT OWNER, TABLE_NAME
FROM ALL_TABLES
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND TABLE_NAME IN (
	'STANDARD_MERGE_SERIES_IDS',
	'STANDARD_MERGE_VERSION_IDS',
	'STANDARD_MERGE_BATCH_IDS',
	'STANDARD_MERGE_TEMPLATE_IDS'
  )
ORDER BY TABLE_NAME;

-- ============================================================
-- 八、删除后核验：规范表应查询不到记录。
-- ============================================================
SELECT OWNER, TABLE_NAME
FROM ALL_TABLES
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND TABLE_NAME IN (
	'STANDARD_FAMILIES',
	'STANDARD_SERIES',
	'STANDARD_FLANGE_RECORDS',
	'STANDARD_CATEGORIES',
	'STANDARD_DOCUMENT_VERSIONS',
	'STANDARD_DOCUMENT_FILES',
	'STANDARD_OPERATION_LOGS',
	'STANDARD_TEMPLATES',
	'STANDARD_TEMPLATE_COLUMNS',
	'STANDARD_IMPORT_BATCHES',
	'STANDARD_IMPORT_ROWS',
	'STANDARD_DYNAMIC_VERSION_ROWS'
  )
ORDER BY TABLE_NAME;

-- ============================================================
-- 九、重建提示：请在本脚本成功后按以下顺序执行建表脚本。
-- 1. standard_schema_dm.sql
-- 2. standard_management_schema_dm.sql
-- 3. standard_template_schema_dm.sql
-- ============================================================

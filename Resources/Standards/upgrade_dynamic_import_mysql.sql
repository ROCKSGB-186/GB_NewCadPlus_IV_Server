-- 动态规范导入结构升级脚本：MySQL 版本
-- 适用：已经执行过旧版 standard_template_schema_mysql.sql 的数据库
-- 说明：脚本只增加动态导入所需字段和版本行表，不删除、不修改既有规范数据。
-- 执行账号需要 ALTER、CREATE、INDEX、REFERENCES 权限。

SET @schema_name = DATABASE();

-- 1. 为模板表增加来源文件名字段。
SET @sql = (
	SELECT IF(COUNT(*) = 0,
		'ALTER TABLE standard_templates ADD COLUMN source_file_name VARCHAR(255) NULL',
		'SELECT 1')
	FROM information_schema.tables
	WHERE table_schema = @schema_name AND table_name = 'standard_templates');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 2. 为模板表增加唯一键字段。
SET @sql = (
	SELECT IF(COUNT(*) = 0,
		'ALTER TABLE standard_templates ADD COLUMN unique_key_fields_json JSON NULL',
		'SELECT 1')
	FROM information_schema.columns
	WHERE table_schema = @schema_name
	  AND table_name = 'standard_templates'
	  AND column_name = 'unique_key_fields_json');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 3. 为批次表增加更新策略字段。
SET @sql = (
	SELECT IF(COUNT(*) = 0,
		'ALTER TABLE standard_import_batches ADD COLUMN update_strategy VARCHAR(16) NOT NULL DEFAULT ''REPLACE''',
		'SELECT 1')
	FROM information_schema.tables
	WHERE table_schema = @schema_name AND table_name = 'standard_import_batches');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 4. 为批次表增加差异摘要字段。
SET @sql = (
	SELECT IF(COUNT(*) = 0,
		'ALTER TABLE standard_import_batches ADD COLUMN difference_json JSON NULL',
		'SELECT 1')
	FROM information_schema.columns
	WHERE table_schema = @schema_name
	  AND table_name = 'standard_import_batches'
	  AND column_name = 'difference_json');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 5. 创建动态版本行表。该表依赖规范版本表、动态导入批次表。
CREATE TABLE IF NOT EXISTS standard_dynamic_version_rows (
	row_id BIGINT NOT NULL AUTO_INCREMENT,
	version_id BIGINT NOT NULL,
	row_number INT NOT NULL,
	unique_key_json JSON NULL,
	values_json JSON NOT NULL,
	source_batch_id CHAR(32) NULL,
	created_at DATETIME NOT NULL,
	PRIMARY KEY (row_id),
	KEY ix_standard_dynamic_version_rows_version (version_id),
	CONSTRAINT fk_standard_dynamic_version_rows_version
		FOREIGN KEY (version_id) REFERENCES standard_document_versions(id),
	CONSTRAINT fk_standard_dynamic_version_rows_batch
		FOREIGN KEY (source_batch_id) REFERENCES standard_import_batches(batch_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 6. 检查升级结果。
SELECT 'standard_templates' AS object_name, 'source_file_name' AS field_name
WHERE EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = @schema_name AND table_name = 'standard_templates' AND column_name = 'source_file_name');

SELECT 'standard_templates' AS object_name, 'unique_key_fields_json' AS field_name
WHERE EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = @schema_name AND table_name = 'standard_templates' AND column_name = 'unique_key_fields_json');

SELECT 'standard_import_batches' AS object_name, 'update_strategy' AS field_name
WHERE EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = @schema_name AND table_name = 'standard_import_batches' AND column_name = 'update_strategy');

SELECT 'standard_import_batches' AS object_name, 'difference_json' AS field_name
WHERE EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = @schema_name AND table_name = 'standard_import_batches' AND column_name = 'difference_json');

SELECT table_name
FROM information_schema.tables
WHERE table_schema = @schema_name
  AND table_name = 'standard_dynamic_version_rows';

-- 7. 可选：确认动态版本行表的外键。
SELECT constraint_name, table_name, referenced_table_name
FROM information_schema.referential_constraints
WHERE constraint_schema = @schema_name
  AND table_name = 'standard_dynamic_version_rows';

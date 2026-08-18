-- 动态规范系列归并并重置：MySQL 8 / InnoDB
-- 目标示例：板式平焊钢制管法兰 + GB/T 9124.1-2019
-- 说明：本脚本会删除目标规范相关的版本、附件、动态批次、动态行、法兰记录和批次关联模板数据。
-- 执行前必须备份数据库；脚本只生成/检查，不由客户端自动执行。
-- 如果目标名称或标准号不同，请修改下面两个变量。

SET @base_series_name = '板式平焊钢制管法兰';
SET @base_standard_number = 'GB/T 9124.1-2019';
SET @operator_name = 'series-merge-reset';

DROP TEMPORARY TABLE IF EXISTS tmp_merge_series_ids;
DROP TEMPORARY TABLE IF EXISTS tmp_merge_version_ids;
DROP TEMPORARY TABLE IF EXISTS tmp_merge_batch_ids;
DROP TEMPORARY TABLE IF EXISTS tmp_merge_template_ids;

CREATE TEMPORARY TABLE tmp_merge_series_ids (
	id BIGINT PRIMARY KEY
);
CREATE TEMPORARY TABLE tmp_merge_version_ids (
	id BIGINT PRIMARY KEY
);
CREATE TEMPORARY TABLE tmp_merge_batch_ids (
	batch_id CHAR(32) PRIMARY KEY
);
CREATE TEMPORARY TABLE tmp_merge_template_ids (
	id BIGINT PRIMARY KEY
);

INSERT INTO tmp_merge_series_ids (id)
SELECT id
FROM standard_series
WHERE is_active = 1
  AND LOWER(TRIM(standard_number)) = LOWER(TRIM(@base_standard_number))
	AND LOWER(TRIM(series_name)) LIKE CONCAT(LOWER(TRIM(@base_series_name)), '%');

-- 执行前检查：确认旧系列和基础系列。若分类数量大于 1，应先人工确认后停止执行。
SELECT s.id, s.category_id, s.family_id, s.series_code, s.series_name,
	   s.standard_number, s.table_number, s.pressure_rating, s.is_active
FROM standard_series s
JOIN tmp_merge_series_ids x ON x.id = s.id
ORDER BY s.id;

SELECT COUNT(DISTINCT COALESCE(category_id, 0)) AS category_count
FROM standard_series s
JOIN tmp_merge_series_ids x ON x.id = s.id;

-- 若上面的 category_count > 1，请不要继续执行；先人工确定归属分类。
START TRANSACTION;

-- 防止误把其他同标准号数据纳入：本次只处理名称以前缀开头的目标系列。
DELETE FROM tmp_merge_series_ids;
INSERT INTO tmp_merge_series_ids (id)
SELECT id
FROM standard_series
WHERE is_active = 1
  AND LOWER(TRIM(standard_number)) = LOWER(TRIM(@base_standard_number))
  AND LOWER(TRIM(series_name)) LIKE CONCAT(LOWER(TRIM(@base_series_name)), '%');

-- 基础系列必须唯一；如果已存在则复用，否则从旧系列复制 family/category。
SET @base_series_id = (
	SELECT MIN(id)
	FROM standard_series
	WHERE is_active = 1
	  AND LOWER(TRIM(series_name)) = LOWER(TRIM(@base_series_name))
	  AND LOWER(TRIM(standard_number)) = LOWER(TRIM(@base_standard_number))
);

SET @family_id = (SELECT MIN(family_id) FROM standard_series JOIN tmp_merge_series_ids x ON x.id = standard_series.id);
SET @category_id = (SELECT MIN(category_id) FROM standard_series JOIN tmp_merge_series_ids x ON x.id = standard_series.id);
SET @series_code = CONCAT('DYNAMIC-', REPLACE(REPLACE(UPPER(@base_standard_number), '/', '-'), ' ', ''));

INSERT INTO standard_series
	(family_id, category_id, series_code, series_name, standard_number,
	 table_number, pressure_rating, flange_type, face_type, is_active, created_at, updated_at)
SELECT @family_id, @category_id, @series_code, @base_series_name, @base_standard_number,
	   '', '', NULL, NULL, 1, NOW(), NOW()
WHERE @base_series_id IS NULL;

SET @base_series_id = COALESCE(
	@base_series_id,
	LAST_INSERT_ID()
);

INSERT IGNORE INTO tmp_merge_series_ids (id) VALUES (@base_series_id);

-- 收集目标系列的版本、批次和批次关联模板。
INSERT IGNORE INTO tmp_merge_version_ids (id)
SELECT id FROM standard_document_versions
WHERE series_id IN (SELECT id FROM tmp_merge_series_ids);

INSERT IGNORE INTO tmp_merge_batch_ids (batch_id)
SELECT batch_id FROM standard_import_batches
WHERE series_id IN (SELECT id FROM tmp_merge_series_ids);

INSERT IGNORE INTO tmp_merge_template_ids (id)
SELECT template_id FROM standard_import_batches
WHERE template_id IS NOT NULL
	AND batch_id IN (SELECT batch_id FROM tmp_merge_batch_ids)
  AND NOT EXISTS (
	  SELECT 1 FROM standard_import_batches other_batch
	  WHERE other_batch.template_id = standard_import_batches.template_id
		AND other_batch.batch_id NOT IN (SELECT batch_id FROM tmp_merge_batch_ids)
  );

-- 删除版本下的文件、动态版本行、批次行、批次主记录和操作日志。
DELETE f FROM standard_document_files f
JOIN tmp_merge_version_ids v ON v.id = f.version_id;
DELETE r FROM standard_dynamic_version_rows r
JOIN tmp_merge_version_ids v ON v.id = r.version_id;
DELETE r FROM standard_import_rows r
JOIN tmp_merge_batch_ids b ON b.batch_id = r.batch_id;
DELETE l FROM standard_operation_logs l
WHERE l.series_id IN (SELECT id FROM tmp_merge_series_ids)
   OR l.version_id IN (SELECT id FROM tmp_merge_version_ids);
DELETE FROM standard_import_batches
WHERE batch_id IN (SELECT batch_id FROM tmp_merge_batch_ids);
DELETE FROM standard_document_versions
WHERE id IN (SELECT id FROM tmp_merge_version_ids);

-- 删除本次批次引用的模板字段和模板。
DELETE c FROM standard_template_columns c
JOIN tmp_merge_template_ids t ON t.id = c.template_id;
DELETE FROM standard_templates
WHERE id IN (SELECT id FROM tmp_merge_template_ids)
  AND NOT EXISTS (
	  SELECT 1 FROM standard_import_batches b
	  WHERE b.template_id = standard_templates.id
  );

-- 删除旧基础表数据，保留系列行用于历史 ID/外键兼容；重新导入从基础系列开始。
DELETE r FROM standard_flange_records r
JOIN tmp_merge_series_ids s ON s.id = r.series_id;

-- 旧系列停用；基础系列唯一启用，表号和型号为空。
UPDATE standard_series
SET is_active = CASE WHEN id = @base_series_id THEN 1 ELSE 0 END,
	updated_at = NOW()
WHERE id IN (SELECT id FROM tmp_merge_series_ids);
UPDATE standard_series
SET series_name = @base_series_name,
	standard_number = @base_standard_number,
	table_number = '',
	pressure_rating = '',
	updated_at = NOW()
WHERE id = @base_series_id;

COMMIT;

SELECT id, category_id, family_id, series_code, series_name, standard_number,
	   table_number, pressure_rating, is_active
FROM standard_series
WHERE id = @base_series_id
   OR (LOWER(TRIM(standard_number)) = LOWER(TRIM(@base_standard_number))
	   AND LOWER(TRIM(series_name)) LIKE CONCAT(LOWER(TRIM(@base_series_name)), '%'))
ORDER BY id;

DROP TEMPORARY TABLE IF EXISTS tmp_merge_series_ids;
DROP TEMPORARY TABLE IF EXISTS tmp_merge_version_ids;
DROP TEMPORARY TABLE IF EXISTS tmp_merge_batch_ids;
DROP TEMPORARY TABLE IF EXISTS tmp_merge_template_ids;

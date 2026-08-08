-- GB 设计规范资料管理扩展：MySQL 版本
-- 说明：本脚本只新增目录、版本、附件和操作日志表，不修改现有规范查询表。
-- 执行前请确认数据库账号具备创建表和索引的权限。

CREATE TABLE IF NOT EXISTS standard_categories (
	id BIGINT NOT NULL AUTO_INCREMENT,
	parent_id BIGINT NULL,
	code VARCHAR(100) NOT NULL,
	name VARCHAR(255) NOT NULL,
	description VARCHAR(500) NULL,
	sort_order INT NOT NULL DEFAULT 0,
	is_active TINYINT NOT NULL DEFAULT 1,
	created_by VARCHAR(128) NULL,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_standard_categories_parent_code (parent_id, code),
	KEY ix_standard_categories_parent (parent_id, is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 将现有规范系列挂接到专业/类别目录；为空时兼容历史数据。
ALTER TABLE standard_series
	ADD COLUMN IF NOT EXISTS category_id BIGINT NULL,
	ADD KEY ix_standard_series_category (category_id, is_active);

CREATE TABLE IF NOT EXISTS standard_document_versions (
	id BIGINT NOT NULL AUTO_INCREMENT,
	series_id BIGINT NOT NULL,
	version_no VARCHAR(64) NOT NULL,
	version_label VARCHAR(128) NULL,
	change_summary VARCHAR(1000) NULL,
	source_type VARCHAR(32) NOT NULL,
	status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
	is_current TINYINT NOT NULL DEFAULT 0,
	is_deleted TINYINT NOT NULL DEFAULT 0,
	created_by VARCHAR(128) NULL,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_standard_document_versions_series_version (series_id, version_no),
	KEY ix_standard_document_versions_series_current (series_id, is_current, is_deleted),
	CONSTRAINT fk_standard_document_versions_series FOREIGN KEY (series_id) REFERENCES standard_series(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS standard_document_files (
	id BIGINT NOT NULL AUTO_INCREMENT,
	version_id BIGINT NOT NULL,
	file_role VARCHAR(32) NOT NULL DEFAULT 'MAIN',
	original_file_name VARCHAR(255) NOT NULL,
	stored_file_name VARCHAR(255) NOT NULL,
	relative_path VARCHAR(1000) NOT NULL,
	extension VARCHAR(32) NOT NULL,
	content_type VARCHAR(255) NULL,
	file_size BIGINT NOT NULL,
	sha256 CHAR(64) NOT NULL,
	description VARCHAR(1000) NULL,
	is_deleted TINYINT NOT NULL DEFAULT 0,
	created_by VARCHAR(128) NULL,
	created_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	KEY ix_standard_document_files_version (version_id, is_deleted),
	KEY ix_standard_document_files_hash (sha256),
	CONSTRAINT fk_standard_document_files_version FOREIGN KEY (version_id) REFERENCES standard_document_versions(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS standard_operation_logs (
	id BIGINT NOT NULL AUTO_INCREMENT,
	operation_type VARCHAR(64) NOT NULL,
	category_id BIGINT NULL,
	series_id BIGINT NULL,
	version_id BIGINT NULL,
	file_id BIGINT NULL,
	operator_name VARCHAR(128) NULL,
	detail_json JSON NULL,
	created_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	KEY ix_standard_operation_logs_created_at (created_at),
	KEY ix_standard_operation_logs_target (series_id, version_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

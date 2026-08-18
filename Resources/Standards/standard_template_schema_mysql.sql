-- 规范导入模板与批次：MySQL 版本
-- 本脚本只新增模板和导入批次表，不修改现有法兰规范表。

CREATE TABLE IF NOT EXISTS standard_templates (
	id BIGINT NOT NULL AUTO_INCREMENT,
	template_code VARCHAR(128) NOT NULL,
	template_name VARCHAR(255) NOT NULL,
	family_code VARCHAR(64) NOT NULL,
	file_type VARCHAR(16) NOT NULL DEFAULT 'XLSX',
	version INT NOT NULL DEFAULT 1,
	is_active TINYINT NOT NULL DEFAULT 1,
	description VARCHAR(1000) NULL,
	source_file_name VARCHAR(255) NULL,
	unique_key_fields_json JSON NULL,
	created_by VARCHAR(128) NULL,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_standard_templates_code_version (template_code, version),
	KEY ix_standard_templates_family (family_code, is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS standard_template_columns (
	id BIGINT NOT NULL AUTO_INCREMENT,
	template_id BIGINT NOT NULL,
	field_code VARCHAR(128) NOT NULL,
	field_name VARCHAR(255) NOT NULL,
	data_type VARCHAR(32) NOT NULL DEFAULT 'TEXT',
	unit VARCHAR(32) NULL,
	is_required TINYINT NOT NULL DEFAULT 0,
	sort_order INT NOT NULL DEFAULT 0,
	header_aliases_json JSON NULL,
	validation_json JSON NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_standard_template_columns_field (template_id, field_code),
	KEY ix_standard_template_columns_order (template_id, sort_order),
	CONSTRAINT fk_standard_template_columns_template FOREIGN KEY (template_id) REFERENCES standard_templates(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS standard_import_rows (
	row_id BIGINT NOT NULL AUTO_INCREMENT,
	batch_id CHAR(32) NOT NULL,
	row_number INT NOT NULL,
	values_json JSON NOT NULL,
	errors_json JSON NULL,
	warnings_json JSON NULL,
	created_at DATETIME NOT NULL,
	PRIMARY KEY (row_id),
	UNIQUE KEY uk_standard_import_rows_batch_row (batch_id, row_number),
	CONSTRAINT fk_standard_import_rows_batch FOREIGN KEY (batch_id) REFERENCES standard_import_batches(batch_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS standard_import_batches (
	batch_id CHAR(32) NOT NULL,
	series_id BIGINT NOT NULL,
	version_id BIGINT NULL,
	template_id BIGINT NULL,
	family_code VARCHAR(64) NOT NULL,
	status VARCHAR(32) NOT NULL DEFAULT 'PREVIEW',
	row_count INT NOT NULL DEFAULT 0,
	error_count INT NOT NULL DEFAULT 0,
	warning_count INT NOT NULL DEFAULT 0,
	update_strategy VARCHAR(16) NOT NULL DEFAULT 'REPLACE',
	difference_json JSON NULL,
	source_file_name VARCHAR(255) NULL,
	source_file_sha256 CHAR(64) NULL,
	created_by VARCHAR(128) NULL,
	created_at DATETIME NOT NULL,
	expires_at DATETIME NULL,
	PRIMARY KEY (batch_id),
	KEY ix_standard_import_batches_status (status, expires_at),
	CONSTRAINT fk_standard_import_batches_series FOREIGN KEY (series_id) REFERENCES standard_series(id),
	CONSTRAINT fk_standard_import_batches_template FOREIGN KEY (template_id) REFERENCES standard_templates(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

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
	CONSTRAINT fk_standard_dynamic_version_rows_version FOREIGN KEY (version_id) REFERENCES standard_document_versions(id),
	CONSTRAINT fk_standard_dynamic_version_rows_batch FOREIGN KEY (source_batch_id) REFERENCES standard_import_batches(batch_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

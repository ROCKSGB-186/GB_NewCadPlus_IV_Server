-- 规范库基础表：MySQL 版本
-- 说明：规范库独立于 CAD_FILE_STORAGE 和 CAD_BLOCK_ATTRIBUTES_JSON，避免影响现有图元数据。

CREATE TABLE IF NOT EXISTS standard_families (
	id BIGINT NOT NULL AUTO_INCREMENT,
	code VARCHAR(64) NOT NULL,
	name VARCHAR(128) NOT NULL,
	description VARCHAR(500) NULL,
	is_active TINYINT NOT NULL DEFAULT 1,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_standard_families_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS standard_series (
	id BIGINT NOT NULL AUTO_INCREMENT,
	family_id BIGINT NOT NULL,
	series_code VARCHAR(100) NOT NULL,
	series_name VARCHAR(255) NOT NULL,
	standard_number VARCHAR(100) NOT NULL,
	table_number VARCHAR(64) NOT NULL,
	pressure_rating VARCHAR(64) NOT NULL,
	flange_type VARCHAR(64) NULL,
	face_type VARCHAR(64) NULL,
	is_active TINYINT NOT NULL DEFAULT 1,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_standard_series_identity (family_id, series_code, standard_number, table_number, pressure_rating),
	KEY ix_standard_series_query (series_code, standard_number, pressure_rating)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS standard_flange_records (
	id BIGINT NOT NULL AUTO_INCREMENT,
	series_id BIGINT NOT NULL,
	source_row_number INT NOT NULL,
	dn VARCHAR(32) NOT NULL,
	dn_value INT NOT NULL,
	pn VARCHAR(64) NOT NULL,
	pipe_outer_diameter_i DECIMAL(18,4) NULL,
	pipe_outer_diameter_ii DECIMAL(18,4) NULL,
	flange_outer_diameter DECIMAL(18,4) NULL,
	bolt_circle_diameter DECIMAL(18,4) NULL,
	bolt_hole_diameter DECIMAL(18,4) NULL,
	bolt_count INT NULL,
	bolt_specification VARCHAR(64) NULL,
	bolt_raw_suffix VARCHAR(128) NULL,
	flange_thickness DECIMAL(18,4) NULL,
	raised_face_height DECIMAL(18,4) NULL,
	flange_inner_diameter_i DECIMAL(18,4) NULL,
	flange_inner_diameter_ii DECIMAL(18,4) NULL,
	raw_values_json LONGTEXT NULL,
	warnings_json LONGTEXT NULL,
	is_active TINYINT NOT NULL DEFAULT 1,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_standard_flange_record_identity (series_id, dn_value, pn),
	KEY ix_standard_flange_record_query (series_id, dn_value, pn)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- CAD_SW_LIBRARY 测试服务分块清理脚本：达梦 DM
-- 破坏性操作：执行前确认当前连接是测试库，并完成备份。
-- DDL 会隐式提交，不能依赖事务回滚。
-- 执行方式：先执行区块 1，再按需执行区块 2 至区块 6；最后必须执行区块 6 的外键恢复部分。
-- 为避免外键长时间处于禁用状态，建议一次执行需要的全部区块。
-- 本版本明确保留：USERS、DEPARTMENTS、SYSTEM_CONFIG、STANDARD_TEMPLATES、STANDARD_TEMPLATE_COLUMNS。

-- ============================================================
-- 区块 1：盘点并解除外键约束
-- ============================================================
-- 先执行以下查询，确认当前 Schema、表和外键符合预期。
SELECT USER AS CURRENT_USER FROM DUAL;

SELECT TABLE_NAME
FROM ALL_TABLES
WHERE OWNER = 'CAD_SW_LIBRARY'
ORDER BY TABLE_NAME;

SELECT OWNER, TABLE_NAME, CONSTRAINT_NAME, STATUS
FROM ALL_CONSTRAINTS
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND CONSTRAINT_TYPE = 'R'
ORDER BY TABLE_NAME, CONSTRAINT_NAME;

-- 动态禁用 CAD_SW_LIBRARY 下当前已启用的全部外键。
DECLARE
BEGIN
  FOR R IN (
    SELECT OWNER, TABLE_NAME, CONSTRAINT_NAME
    FROM ALL_CONSTRAINTS
    WHERE OWNER = 'CAD_SW_LIBRARY'
      AND CONSTRAINT_TYPE = 'R'
      AND STATUS = 'ENABLED'
  ) LOOP
    EXECUTE IMMEDIATE
      'ALTER TABLE ' || R.OWNER || '.' || R.TABLE_NAME ||
      ' DISABLE CONSTRAINT ' || R.CONSTRAINT_NAME;
  END LOOP;
END;
/

-- ============================================================
-- 区块 2：清理分类相关数据
-- ============================================================
-- 清理 CAD/SW 分类、子分类、分类统计和分类模板关联。
-- 不清理 DEPARTMENTS、USERS。
DECLARE
BEGIN
  FOR R IN (
    SELECT TABLE_NAME
    FROM ALL_TABLES
    WHERE OWNER = 'CAD_SW_LIBRARY'
      AND TABLE_NAME IN (
        'CATEGORY_ATTRIBUTE_TEMPLATES',
        'CATEGORY_DEPARTMENT_MAP',
        'CATEGORY_STATISTICS',
        'CAD_CATEGORIES',
        'CAD_SUBCATEGORIES',
        'SW_CATEGORIES',
        'SW_SUBCATEGORIES'
      )
  ) LOOP
    EXECUTE IMMEDIATE 'TRUNCATE TABLE CAD_SW_LIBRARY.' || R.TABLE_NAME;
  END LOOP;
END;
/

-- ============================================================
-- 区块 3：清理部门与人员关联数据
-- ============================================================
-- 按当前需求只清理部门-人员关联，不删除部门和用户主数据。
DECLARE
BEGIN
  FOR R IN (
    SELECT TABLE_NAME
    FROM ALL_TABLES
    WHERE OWNER = 'CAD_SW_LIBRARY'
      AND TABLE_NAME IN ('DEPARTMENT_USERS')
  ) LOOP
    EXECUTE IMMEDIATE 'TRUNCATE TABLE CAD_SW_LIBRARY.' || R.TABLE_NAME;
  END LOOP;
END;
/

-- ============================================================
-- 区块 4：清理储存文件与相关属性数据
-- ============================================================
-- 清理文件实体、文件版本、文件访问日志、标签、属性值和块属性 JSON。
-- 同时清理属性定义及模板实例数据；保留标准导入模板表由区块 6 说明。
DECLARE
BEGIN
  FOR R IN (
    SELECT TABLE_NAME
    FROM ALL_TABLES
    WHERE OWNER = 'CAD_SW_LIBRARY'
      AND TABLE_NAME IN (
        'CAD_BLOCK_ATTRIBUTES_JSON',
        'CAD_FILE_STORAGE',
        'FILE_ACCESS_LOGS',
        'FILE_ATTRIBUTE_VALUES',
        'FILE_TAGS',
        'FILE_VERSION_HISTORY',
        'ATTRIBUTE_TEMPLATE_ITEMS',
        'ATTRIBUTE_TEMPLATES',
        'ATTRIBUTE_DEFINITIONS'
      )
  ) LOOP
    EXECUTE IMMEDIATE 'TRUNCATE TABLE CAD_SW_LIBRARY.' || R.TABLE_NAME;
  END LOOP;
END;
/

-- ============================================================
-- 区块 5：清理设计标准的分类层级数据
-- ============================================================
-- 只清理设计标准分类目录，不删除标准模板和模板字段。
DECLARE
BEGIN
  FOR R IN (
    SELECT TABLE_NAME
    FROM ALL_TABLES
    WHERE OWNER = 'CAD_SW_LIBRARY'
      AND TABLE_NAME IN ('STANDARD_CATEGORIES')
  ) LOOP
    EXECUTE IMMEDIATE 'TRUNCATE TABLE CAD_SW_LIBRARY.' || R.TABLE_NAME;
  END LOOP;
END;
/

-- ============================================================
-- 区块 6：清理设计标准数据，并恢复外键
-- ============================================================
-- 清理标准系列、规范文档、版本、文件、明细和导入运行数据。
-- 保留 STANDARD_TEMPLATES、STANDARD_TEMPLATE_COLUMNS。
DECLARE
BEGIN
  FOR R IN (
    SELECT TABLE_NAME
    FROM ALL_TABLES
    WHERE OWNER = 'CAD_SW_LIBRARY'
      AND TABLE_NAME IN (
        'STANDARD_DYNAMIC_VERSION_ROWS',
        'STANDARD_IMPORT_ROWS',
        'STANDARD_DOCUMENT_FILES',
        'STANDARD_IMPORT_BATCHES',
        'STANDARD_DOCUMENT_VERSIONS',
        'STANDARD_OPERATION_LOGS',
        'STANDARD_FLANGE_RECORDS',
        'STANDARD_SERIES',
        'STANDARD_DOCUMENTS',
        'STANDARD_FAMILIES'
      )
  ) LOOP
    EXECUTE IMMEDIATE 'TRUNCATE TABLE CAD_SW_LIBRARY.' || R.TABLE_NAME;
  END LOOP;
END;
/

-- 区块 6 的收尾：重新启用全部外键。
DECLARE
BEGIN
  FOR R IN (
    SELECT OWNER, TABLE_NAME, CONSTRAINT_NAME
    FROM ALL_CONSTRAINTS
    WHERE OWNER = 'CAD_SW_LIBRARY'
      AND CONSTRAINT_TYPE = 'R'
      AND STATUS = 'DISABLED'
  ) LOOP
    EXECUTE IMMEDIATE
      'ALTER TABLE ' || R.OWNER || '.' || R.TABLE_NAME ||
      ' ENABLE CONSTRAINT ' || R.CONSTRAINT_NAME;
  END LOOP;
END;
/

-- ============================================================
-- 执行后核验
-- ============================================================
-- 以下查询应返回 0 行；NUM_ROWS 依赖统计信息，必要时再对具体表执行 COUNT(*)。
SELECT TABLE_NAME, NUM_ROWS
FROM ALL_TABLES
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND TABLE_NAME IN (
    'CATEGORY_ATTRIBUTE_TEMPLATES', 'CATEGORY_DEPARTMENT_MAP', 'CATEGORY_STATISTICS',
    'CAD_CATEGORIES', 'CAD_SUBCATEGORIES', 'SW_CATEGORIES', 'SW_SUBCATEGORIES',
    'DEPARTMENT_USERS', 'CAD_BLOCK_ATTRIBUTES_JSON', 'CAD_FILE_STORAGE',
    'FILE_ACCESS_LOGS', 'FILE_ATTRIBUTE_VALUES', 'FILE_TAGS', 'FILE_VERSION_HISTORY',
    'ATTRIBUTE_TEMPLATE_ITEMS', 'ATTRIBUTE_TEMPLATES', 'ATTRIBUTE_DEFINITIONS',
    'STANDARD_CATEGORIES', 'STANDARD_DYNAMIC_VERSION_ROWS', 'STANDARD_IMPORT_ROWS',
    'STANDARD_DOCUMENT_FILES', 'STANDARD_IMPORT_BATCHES', 'STANDARD_DOCUMENT_VERSIONS',
    'STANDARD_OPERATION_LOGS', 'STANDARD_FLANGE_RECORDS', 'STANDARD_SERIES',
    'STANDARD_DOCUMENTS', 'STANDARD_FAMILIES'
  )
ORDER BY TABLE_NAME;

SELECT TABLE_NAME, CONSTRAINT_NAME, STATUS
FROM ALL_CONSTRAINTS
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND CONSTRAINT_TYPE = 'R'
  AND STATUS <> 'ENABLED'
ORDER BY TABLE_NAME, CONSTRAINT_NAME;
-- 最后一条查询应返回 0 行。

-- 明确保留、不会被本脚本清理的表：
-- USERS、DEPARTMENTS、SYSTEM_CONFIG、STANDARD_TEMPLATES、STANDARD_TEMPLATE_COLUMNS。

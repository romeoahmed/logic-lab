-- Run with psql -v ON_ERROR_STOP=1 -f benchmarks/catalog-pagination.sql.
-- Uses only session-local data; compare index conditions and rows filtered, not timings.
CREATE TEMP TABLE catalog_plan_sample (
    subject_id varchar(512) NOT NULL,
    display_name varchar(256) NOT NULL,
    display_name_sort_key bytea NOT NULL,
    durable_project_id varchar(64) COLLATE "C" NOT NULL
);
CREATE UNIQUE INDEX catalog_plan_sample_order ON catalog_plan_sample
    (subject_id, display_name_sort_key, durable_project_id);
INSERT INTO catalog_plan_sample
SELECT 'subject-1', lpad((ordinal / 2)::text, 8, '0'),
       convert_to(lpad((ordinal / 2)::text, 8, '0'), 'UTF8'),
       lpad(ordinal::text, 32, '0')
FROM generate_series(1, 100000) AS ordinal;
ANALYZE catalog_plan_sample;

-- Previous disjunction expands the two-field ordering manually.
EXPLAIN (ANALYZE, BUFFERS, TIMING OFF)
SELECT durable_project_id, display_name, display_name_sort_key
FROM catalog_plan_sample
WHERE subject_id = 'subject-1'
    AND (display_name_sort_key > convert_to('00045000', 'UTF8')
        OR (display_name_sort_key = convert_to('00045000', 'UTF8')
            AND durable_project_id > '00000000000000000000000000090000'))
ORDER BY display_name_sort_key, durable_project_id LIMIT 21;

-- Retained tuple comparison uses the same ordering and cursor.
EXPLAIN (ANALYZE, BUFFERS, TIMING OFF)
SELECT durable_project_id, display_name, display_name_sort_key
FROM catalog_plan_sample
WHERE subject_id = 'subject-1'
    AND (display_name_sort_key, durable_project_id)
        > (convert_to('00045000', 'UTF8'), '00000000000000000000000000090000')
ORDER BY display_name_sort_key, durable_project_id LIMIT 21;

-- Database roles for the URL shortener. Run once, as a superuser, on a fresh database (the Postgres image does this from
-- /docker-entrypoint-initdb.d once the deploy script has filled in the placeholders).
--
--   {{DATABASE}}        name of the database
--   {{WRITE_PASSWORD}}  password of urlshortener_write (the Write Service: owns the schema, runs the migrations)
--   {{READ_PASSWORD}}   password of urlshortener_read  (the Read Service: SELECT only)
--
-- Placeholders only: real passwords live in deploy/.env on the server and are never committed.
-- Use letters and digits in passwords (no quotes), as they are substituted textually.

CREATE ROLE urlshortener_write LOGIN PASSWORD '{{WRITE_PASSWORD}}';
CREATE ROLE urlshortener_read  LOGIN PASSWORD '{{READ_PASSWORD}}';

REVOKE ALL ON DATABASE {{DATABASE}} FROM PUBLIC;
GRANT CONNECT ON DATABASE {{DATABASE}} TO urlshortener_write, urlshortener_read;

GRANT USAGE, CREATE ON SCHEMA public TO urlshortener_write;
GRANT USAGE ON SCHEMA public TO urlshortener_read;

-- Every table the Write Service creates (now or in a later migration) is readable, and only readable, by the Read Service.
ALTER DEFAULT PRIVILEGES FOR ROLE urlshortener_write IN SCHEMA public GRANT SELECT ON TABLES TO urlshortener_read;

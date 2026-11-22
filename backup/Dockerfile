# DollarShark backup image: PostgreSQL client (pg_dump/pg_restore) + gpg (encryption) + AWS CLI (S3 upload).
# Runs as the "backup" service in docker-compose.yml — see run.sh for the schedule.
FROM alpine:3.22

RUN apk add --no-cache postgresql17-client gnupg aws-cli tzdata

COPY run.sh backup.sh restore.sh /usr/local/bin/
# Strip Windows line endings in case the repo was checked out on Windows, then make the scripts executable.
RUN sed -i 's/\r$//' /usr/local/bin/*.sh && chmod 755 /usr/local/bin/*.sh

ENTRYPOINT ["run.sh"]

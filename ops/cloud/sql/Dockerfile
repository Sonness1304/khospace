FROM mcr.microsoft.com/mssql/server:2022-latest@sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718
USER root
RUN apt-get update && apt-get install -y --no-install-recommends util-linux && \
    rm -rf /var/lib/apt/lists/*
COPY start-sql.sh /usr/local/bin/start-khospace-sql
RUN chmod 755 /usr/local/bin/start-khospace-sql
ENV MSSQL_PID=Express
ENTRYPOINT ["/usr/local/bin/start-khospace-sql"]

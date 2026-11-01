FROM postgres:17.6-bookworm
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/* \
    && mkdir /logs
COPY deploy/log_runner.py /usr/local/bin/log_runner.py
ENTRYPOINT ["python3", "/usr/local/bin/log_runner.py", "docker-entrypoint.sh"]
CMD ["postgres"]

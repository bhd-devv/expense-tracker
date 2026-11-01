FROM caddy:2.10.2-alpine
RUN apk add --no-cache python3 && mkdir /logs
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY deploy/log_runner.py /usr/local/bin/log_runner.py
ENTRYPOINT ["python3", "/usr/local/bin/log_runner.py"]
CMD ["caddy", "run", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"]

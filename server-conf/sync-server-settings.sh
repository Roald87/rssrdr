#!/bin/bash

# Script to copy server settings to the current directory

# Source files
FAIL2BAN_CONF="/etc/fail2ban/jail.local"
NGINX_CONF="/etc/nginx/nginx.conf"
SYSTEMD_SERVICE="/etc/systemd/system/rssrdr-server.service"

# Target directory (current directory)
TARGET_DIR=$(pwd)

cp -i "$FAIL2BAN_CONF" "$TARGET_DIR/jail.local"
cp -i "$NGINX_CONF" "$TARGET_DIR/nginx.conf"
cp -i "$SYSTEMD_SERVICE" "$TARGET_DIR/rssrdr-server.service"

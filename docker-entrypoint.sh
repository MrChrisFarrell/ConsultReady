#!/bin/sh
set -eu
mkdir -p /var/data
chown app:app /var/data
exec gosu app dotnet ConsultReady.Api.dll
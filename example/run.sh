#!/usr/bin/env bash

set -e

# Service identification
DOMAIN=consents
CONTEXT=ftracing
PURPOSE=example
VERSION=local
ENVIRONMENT=dev1-services

# Logging common
LOG_TO="console"
VERBOSITY=vvv

###
### Open Telementry Tracing
###
export TRACING_THRIFT_HOST="tracing-thrift.service.$ENVIRONMENT.consul:80"
export TRACING_SERVICE_NAME="$DOMAIN-$CONTEXT"
export TRACING_TAGS="svc_domain=$DOMAIN,svc_context=$CONTEXT,svc_purpose=$PURPOSE,svc_version=$VERSION"
export TRACING_LOG_TO="$LOG_TO"
export TRACING_LOG_META="$LOGGER_TAGS"
export TRACING_LOG_LEVEL="$VERBOSITY"

export TRACING_EXPORT_CONSOLE="on"

dotnet watch run

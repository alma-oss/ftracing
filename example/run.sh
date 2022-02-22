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

# Tracing specific
export JAEGER_GRPC_TARGET="tracing-grpc.service.$ENVIRONMENT.consul:80"
export JAEGER_SERVICE_NAME="$DOMAIN-$CONTEXT"
export JAEGER_TAGS="svc_domain=$DOMAIN,svc_context=$CONTEXT,svc_purpose=$PURPOSE,svc_version=$VERSION"

# Tracing common
export JAEGER_LOG_TO="$LOG_TO"
export JAEGER_TRACEID_128BIT=1
export JAEGER_SAMPLER_PARAM=1
export JAEGER_SAMPLER_TYPE=const
export JAEGER_SENDER_FACTORY=grpc
export JAEGER_PROPAGATION=b3
export JAEGER_LOG_META="$LOGGER_TAGS"
export JAEGER_LOG_LEVEL="$VERBOSITY"

dotnet watch run

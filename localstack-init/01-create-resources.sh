#!/bin/bash
set -euo pipefail

# Same region the service signs requests for (AWS_DEFAULT_REGION in docker-compose.yml).
REGION=eu-central-1
ACCOUNT=000000000000

awslocal --region "$REGION" sqs create-queue --queue-name gamex-activity-events-dlq
awslocal --region "$REGION" sqs create-queue --queue-name gamex-activity-events \
  --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"arn:aws:sqs:$REGION:$ACCOUNT:gamex-activity-events-dlq\\\",\\\"maxReceiveCount\\\":\\\"3\\\"}\"}"

#!/bin/bash

echo "export SB_SAMPLE_TOPIC=$(pulumi stack output sample -C src/infrastructure/Sweetspot.Infrastructure.Core)" > dev.env
echo "export SB_SAMPLE_ENDPOINT_LISTEN=$(pulumi stack output sample_listen_endpoint -C src/infrastructure/Sweetspot.Infrastructure.Core --show-secrets)" >> dev.env
echo "export SB_SAMPLE_ENDPOINT_SEND=$(pulumi stack output sample_send_endpoint -C src/infrastructure/Sweetspot.Infrastructure.Core --show-secrets)" >> dev.env

echo "===="
echo ">>>dev.env file created"
echo ">>>To set the env variables run '> source dev.env'"
#!/bin/bash

pulumi stack -s dev output kubeconfig --show-secrets -C src/infrastructure/Sweetspot.Infrastructure.Core > kubeconfig

echo "export SB_SAMPLE_TOPIC=$(pulumi stack -s dev output sample -C src/infrastructure/Sweetspot.Infrastructure.Core)" > dev.env
echo "export SB_SAMPLE_ENDPOINT_LISTEN=$(pulumi stack -s dev output sample_listen_endpoint -C src/infrastructure/Sweetspot.Infrastructure.Core --show-secrets)" >> dev.env
echo "export SB_SAMPLE_ENDPOINT_SEND=$(pulumi stack -s dev output sample_send_endpoint -C src/infrastructure/Sweetspot.Infrastructure.Core --show-secrets)" >> dev.env
echo "export KUBECONFIG=$(pwd)/kubeconfig" >> dev.env

echo "===="
echo ">>>dev.env file created"
echo ">>>To set the env variables run '> source dev.env'"

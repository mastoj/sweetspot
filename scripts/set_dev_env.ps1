pulumi stack -s dev output kubeconfig --show-secrets -C src/infrastructure/Sweetspot.Infrastructure.Core > kubeconfig

$env:SB_SAMPLE_TOPIC = $(pulumi stack -s dev output sample -C src/infrastructure/Sweetspot.Infrastructure.Core)
$env:SB_SAMPLE_ENDPOINT_LISTEN = $(pulumi stack -s dev output sample_listen_endpoint -C src/infrastructure/Sweetspot.Infrastructure.Core --show-secrets)
$env:SB_SAMPLE_ENDPOINT_SEND = $(pulumi stack -s dev output sample_send_endpoint -C src/infrastructure/Sweetspot.Infrastructure.Core --show-secrets)
$env:SB_SAMPLE_SUBSCRIPTION = $(pulumi stack -s dev output subscription -C .\src\infrastructure\Sweetspot.Infrastructure.Application)
$env:KUBECONFIG = "$(Get-Location)\kubeconfig"

Write-Host "===="
Write-Host ">>>dev.env file created"
Write-Host ">>>To set the env variables run '> source dev.env'"

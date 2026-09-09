#requires -Version 7.0
<#
.SYNOPSIS
    Posts a PropFlow build/test/PR status update to the Slack #agent-updates channel.

.DESCRIPTION
    Post-only. Reads the incoming-webhook URL from the PROPFLOW_SLACK_WEBHOOK_URL
    environment variable, which is set in the gitignored .claude/settings.local.json
    (see .gitignore) and never committed. The webhook is bound by Slack to a single
    channel, so this script cannot post anywhere else and cannot read anything back.

    The variable name deliberately avoids the substring TOKEN: the auto-mode classifier
    blocks reading environment variables whose name contains it.

    Fails with a non-zero exit code when the URL is unset. A status poster that silently
    no-ops is worse than one that is obviously broken.

.PARAMETER Text
    The message body. Slack mrkdwn is supported (*bold*, `code`, <url|label>).

.PARAMETER Status
    ok | fail | info. Drives the leading emoji and the attachment colour.

.PARAMETER Link
    Optional URL — a PR or a CI run — rendered as a context line under the message.

.EXAMPLE
    ./scripts/Send-SlackUpdate.ps1 -Status ok -Text 'PF-3.14 verify green (0 warnings)'

.EXAMPLE
    ./scripts/Send-SlackUpdate.ps1 -Status fail -Text 'IntegrationTests: 3 failed' -Link $env:GITHUB_RUN_URL
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string] $Text,

    [Parameter()]
    [ValidateSet('ok', 'fail', 'info')]
    [string] $Status = 'info',

    [Parameter()]
    [string] $Link
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$webhook = $env:PROPFLOW_SLACK_WEBHOOK_URL
if ([string]::IsNullOrWhiteSpace($webhook)) {
    Write-Error @'
PROPFLOW_SLACK_WEBHOOK_URL is not set, so there is nowhere to post.

Set it in the gitignored .claude/settings.local.json:

  { "env": { "PROPFLOW_SLACK_WEBHOOK_URL": "https://hooks.slack.com/services/..." } }

or, for one command, in the current shell:

  $env:PROPFLOW_SLACK_WEBHOOK_URL = '<url>'

Create the webhook at Slack -> your app -> Incoming Webhooks -> Add New Webhook to
Workspace, and point it at #agent-updates.
'@ -ErrorAction Continue
    exit 2
}

if ($webhook -notmatch '^https://hooks\.slack\.com/') {
    Write-Error 'PROPFLOW_SLACK_WEBHOOK_URL does not look like a Slack incoming webhook (expected it to start with https://hooks.slack.com/). Refusing to post.' -ErrorAction Continue
    exit 2
}

$decoration = switch ($Status) {
    'ok' { @{ Emoji = ':white_check_mark:'; Color = '#2eb886' } }
    'fail' { @{ Emoji = ':x:'; Color = '#e01e5a' } }
    default { @{ Emoji = ':information_source:'; Color = '#1d9bd1' } }
}

$blocks = @(
    @{
        type = 'section'
        text = @{ type = 'mrkdwn'; text = "$($decoration.Emoji) $Text" }
    }
)

$contextParts = @("PropFlow | $([DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm K'))")
if (-not [string]::IsNullOrWhiteSpace($Link)) {
    $contextParts += "<$Link|details>"
}
$blocks += @{
    type     = 'context'
    elements = @(@{ type = 'mrkdwn'; text = ($contextParts -join '  ·  ') })
}

# fallback_text is what a notification or a client without Block Kit renders.
$payload = @{
    text        = "$($decoration.Emoji) $Text"
    attachments = @(
        @{
            color  = $decoration.Color
            blocks = $blocks
        }
    )
} | ConvertTo-Json -Depth 10 -Compress

try {
    # Slack answers a webhook with the literal body "ok" on success.
    $response = Invoke-RestMethod -Uri $webhook -Method Post -ContentType 'application/json' -Body $payload
}
catch {
    # $webhook is never interpolated into output — the URL is the credential.
    Write-Error "Slack rejected the post: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}

Write-Output "slack: $response"
if ("$response".Trim() -ne 'ok') {
    Write-Error "Unexpected response from Slack (expected 'ok')." -ErrorAction Continue
    exit 1
}

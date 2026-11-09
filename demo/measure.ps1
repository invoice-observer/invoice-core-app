#Requires -Version 7
# Logs in, makes one warm-up call to an API endpoint, then measures N further calls.
# Prints min/median/max duration and the response size in KB (1 KB = 1024 bytes).
#
# Run: ./demo/measure.ps1 -BaseUrl https://localhost:7052 -UserName <user> -Password <password> -Endpoint /api/invoices -Count 20
#
# Use the https URL: the app redirects http to https, and the script stops on that redirect.
# Any status other than 2xx stops the script, the warm-up call included.

param(
    [Parameter(Mandatory)] [string] $BaseUrl,
    [Parameter(Mandatory)] [string] $UserName,
    [Parameter(Mandatory)] [string] $Password,
    [Parameter(Mandatory)] [string] $Endpoint,
    [Parameter(Mandatory)] [ValidateRange(1, [int]::MaxValue)] [int] $Count
)

$ErrorActionPreference = 'Stop'

$base = $BaseUrl.TrimEnd('/')
$url = $base + '/' + $Endpoint.TrimStart('/')

$login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' `
    -Body (@{ username = $UserName; password = $Password } | ConvertTo-Json)

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(10)
$client.DefaultRequestHeaders.Authorization =
    [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $login.token)

# HttpClient, not Invoke-RestMethod: parsing the JSON in PowerShell would dominate the timing.
function Invoke-TimedCall {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $response = $client.GetAsync($url).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $watch.Stop()

    if (-not $response.IsSuccessStatusCode) {
        throw "GET $url returned $([int]$response.StatusCode)."
    }

    [pscustomobject]@{ Milliseconds = $watch.Elapsed.TotalMilliseconds; Bytes = $body.Length }
}

try {
    $null = Invoke-TimedCall
    $calls = 1..$Count | ForEach-Object { Invoke-TimedCall }
}
finally {
    $client.Dispose()
}

$sorted = @($calls.Milliseconds | Sort-Object)
$middle = [int][Math]::Floor($sorted.Count / 2)
$median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }

'Endpoint: {0}' -f $url
'Calls:    {0} (after 1 warm-up call)' -f $Count
'Min:      {0:N1} ms' -f $sorted[0]
'Median:   {0:N1} ms' -f $median
'Max:      {0:N1} ms' -f $sorted[-1]
'Size:     {0:N1} KB' -f (@($calls)[-1].Bytes / 1024)

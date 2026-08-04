<#
.SYNOPSIS
    Creates Cycle U's four simulated members and their pre-U data, over HTTP,
    against a running API.

.DESCRIPTION
    W0 asked for four real people who consent to their money living in this
    database. There are none — the only real user is Alp — so the cohort is
    simulated instead. That is a smaller feature to build, not a bigger one:
    nobody else's money is at stake, W3's cross-user write moves only fabricated
    balances, and the plan's closing line about running "with somebody else's"
    data stops being true.

    What simulation does NOT remove is the need for four distinct accounts. Every
    wave from W1 on is verified by two logged-in users watching each other's
    actions, and the largest-remainder tie-break needs a concrete member count.
    Both come from here.

    Members are created through POST api/User/signup, so Identity hashes the
    passwords and UserDataSeeder writes each member's starter catalogue exactly as
    it does for a real signup — no hand-written SQL, no schema knowledge, and the
    rows are indistinguishable from organically created ones.

    Each member ends up with: the seeded EUR catalogue (4 currencies, 11
    categories, one 'Main' account), two salary incomes, five ordinary expenses,
    one split expense carrying an ExpectedReimbursement (Stream R), and one
    savings goal with a contribution (Stream V). That is the "pre-U data" the
    migration rehearsals need, and it covers both streams that landed yesterday.

    Amounts differ per member on purpose: in a two-browser pass you can tell whose
    session you are looking at from the dashboard alone.

.EXAMPLE
    .\u0-seed-cohort.ps1 -BaseUrl http://localhost:2222

.EXAMPLE
    # Re-seed transactions for members that already have some.
    .\u0-seed-cohort.ps1 -BaseUrl http://localhost:2222 -Force

.NOTES
    Start the API with RateLimiting__Auth__PermitLimit=100 — the auth endpoints
    allow 10 requests/minute per IP by default and this makes 8 in a burst. The
    script retries a 429 once after 60s rather than failing outright.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:2222',

    # Meets Identity's default policy: 6+ chars, upper, lower, digit, symbol.
    [string]$Password = 'SimMember1!',

    # Seed transactions even for members that already have some.
    [switch]$Force,

    # For https://localhost:42069 with the dev certificate.
    [switch]$SkipCertificateCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$BaseUrl = $BaseUrl.TrimEnd('/')
$today = [datetime]'2026-08-04'   # pinned: re-runs produce the same ledger
function D([int]$daysAgo) { $today.AddDays(-$daysAgo).ToString('yyyy-MM-dd') }

# ── the cohort ────────────────────────────────────────────────────────────────
#
# Four members: Alp plus three simulated ones. The count is fixed here and it is
# the number every worked example and the rounding tie-break refer to — a €10.00
# expense split four ways is 2.50 each with no remainder, a €10.01 one is
# 2.51/2.50/2.50/2.50 with the extra cent going to the earliest-joined member,
# which is the order below.
#
# Emails use .local so nothing can accidentally reach a real inbox if Stream C's
# IEmailSender is ever wired to a real provider.

$cohort = @(
    @{ Key = 'alp';   First = 'Alp';   Last = 'Balaj';     Email = 'alp@sim.local';   Salary = 2600; Goal = @{ Title = 'Japan trip';     Target = 3000; Contribution = 400 } }
    @{ Key = 'mara';  First = 'Mara';  Last = 'Devlin';    Email = 'mara@sim.local';  Salary = 2200; Goal = @{ Title = 'New laptop';     Target = 1400; Contribution = 250 } }
    @{ Key = 'tomas'; First = 'Tomas'; Last = 'Reyes';     Email = 'tomas@sim.local'; Salary = 1950; Goal = @{ Title = 'Emergency fund'; Target = 5000; Contribution = 300 } }
    @{ Key = 'iris';  First = 'Iris';  Last = 'Lindqvist'; Email = 'iris@sim.local';  Salary = 2400; Goal = @{ Title = 'Winter bike';    Target = 900;  Contribution = 175 } }
)

# Per-member spending. The multiplier keeps every member's ledger a different
# shape while the categories and dates stay comparable across sessions.
$spendPattern = @(
    @{ Title = 'Weekly shop';     Category = 'Groceries';     Amount = 62.40; DaysAgo = 3 }
    @{ Title = 'Dinner out';      Category = 'Eating Out';    Amount = 38.00; DaysAgo = 6 }
    @{ Title = 'Transit pass';    Category = 'Transport';     Amount = 49.00; DaysAgo = 12 }
    @{ Title = 'Electricity';     Category = 'Utilities';     Amount = 71.25; DaysAgo = 18 }
    @{ Title = 'Cinema';          Category = 'Entertainment'; Amount = 24.00; DaysAgo = 25 }
)

# One split bill each — the Stream R path, so W4's third subtraction has something
# to compose with and the reimbursement pins have real rows behind them.
$splitBill = @{ Title = 'Group dinner'; Category = 'Eating Out'; Amount = 96.00; Expected = 72.00; DaysAgo = 8 }

# ── http ──────────────────────────────────────────────────────────────────────

$restArgs = @{}
if ($SkipCertificateCheck) { $restArgs['SkipCertificateCheck'] = $true }

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body,
        [string]$Token,
        # Signup answers a duplicate email with 409, and a rejected registration with a
        # deliberately vague 400 (it must not become an account-enumeration oracle).
        # On a re-run both mean the same thing: this member already exists.
        [switch]$TolerateExisting
    )

    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $call = @{ Method = $Method; Uri = "$BaseUrl/$($Path.TrimStart('/'))"; Headers = $headers } + $restArgs
    if ($null -ne $Body) {
        $call['Body'] = ($Body | ConvertTo-Json -Depth 6)
        $call['ContentType'] = 'application/json'
    }

    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            return Invoke-RestMethod @call
        }
        catch {
            $status = $null
            if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) {
                $status = [int]$_.Exception.Response.StatusCode
            }

            if ($status -eq 429 -and $attempt -eq 1) {
                Write-Host "    rate limited — waiting 60s (start the API with RateLimiting__Auth__PermitLimit=100 to avoid this)" -ForegroundColor Yellow
                Start-Sleep -Seconds 60
                continue
            }

            if ($status -in @(400, 409) -and $TolerateExisting) { return $null }

            throw "$Method $Path failed$(if ($status) { " ($status)" }): $($_.Exception.Message)"
        }
    }
}

<#
    GET a list endpoint as a real array.

    Every list endpoint here answers an empty collection with `[]`, which
    Invoke-RestMethod materialises as an empty Object[] — and a function that emits
    an empty array puts the array *itself* on the pipeline as a single object rather
    than enumerating nothing. So `@(Invoke-Api …)` counts 1 for an empty list, and a
    brand-new member reads as already having an expense. Assigning first and wrapping
    second is what actually flattens it; the leading comma keeps the array intact on
    the way back out.
#>
function Get-ApiList {
    param([string]$Path, [string]$Token)

    $result = Invoke-Api -Method GET -Path $Path -Token $Token
    return , @($result)
}

# ── seed ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Seeding cohort against $BaseUrl" -ForegroundColor Cyan
Write-Host ""

$summary = @()

foreach ($member in $cohort) {
    $name = "$($member.First) $($member.Last)"
    Write-Host "  $name <$($member.Email)>" -ForegroundColor White

    # Signup is idempotent by omission: a tolerated 400/409 here means the member
    # already exists, and the login below proves the account is usable either way.
    $created = $null -ne (Invoke-Api -Method POST -Path 'api/User/signup' -TolerateExisting -Body @{
        firstName = $member.First
        lastName  = $member.Last
        email     = $member.Email
        password  = $Password
    })
    Write-Host "    signup: $(if ($created) { 'created' } else { 'already existed' })" -ForegroundColor DarkGray

    $login = Invoke-Api -Method POST -Path 'api/User/login' -Body @{ email = $member.Email; password = $Password }
    $token = $login.token
    if (-not $token) { throw "Login for $($member.Email) returned no token." }

    $accounts = Get-ApiList -Path 'api/Account' -Token $token
    $main = $accounts | Where-Object { $_.name -eq 'Main' } | Select-Object -First 1
    if (-not $main) { throw "No 'Main' account for $($member.Email) — did UserDataSeeder run?" }

    $categories = Get-ApiList -Path 'api/Category' -Token $token
    $byName = @{}
    foreach ($c in $categories) { $byName["$($c.name)|$($c.categoryType)"] = $c.id }

    # Not named 'Cat': that is a built-in alias for Get-Content, and aliases win
    # over functions in PowerShell's command resolution order.
    function CategoryId([string]$categoryName, [int]$type) {
        $id = $byName["$categoryName|$type"]
        if (-not $id) { throw "Category '$categoryName' (type $type) missing for $($member.Email)." }
        return $id
    }

    $existing = Get-ApiList -Path 'api/Expense' -Token $token
    if ($existing.Count -gt 0 -and -not $Force) {
        Write-Host "    already has $($existing.Count) expenses — skipping (pass -Force to add anyway)" -ForegroundColor DarkGray
        $summary += [pscustomobject]@{ Member = $name; Email = $member.Email; Expenses = $existing.Count; Status = 'existing' }
        continue
    }

    # Income first: expenses posted against a zero-balance account would leave
    # every member permanently negative and make the dashboard useless to read.
    foreach ($daysAgo in @(31, 1)) {
        Invoke-Api -Method POST -Path 'api/Income' -Token $token -Body @{
            amount      = $member.Salary
            date        = (D $daysAgo)
            description = 'Salary'
            accountId   = $main.id
            categoryId  = (CategoryId 'Salary' 1)
        } | Out-Null
    }

    foreach ($row in $spendPattern) {
        Invoke-Api -Method POST -Path 'api/Expense' -Token $token -Body @{
            title      = $row.Title
            amount     = $row.Amount
            date       = (D $row.DaysAgo)
            accountId  = $main.id
            categoryId = (CategoryId $row.Category 0)
        } | Out-Null
    }

    Invoke-Api -Method POST -Path 'api/Expense' -Token $token -Body @{
        title                 = $splitBill.Title
        amount                = $splitBill.Amount
        date                  = (D $splitBill.DaysAgo)
        description           = 'Split three ways — Stream R fixture'
        accountId             = $main.id
        categoryId            = (CategoryId $splitBill.Category 0)
        expectedReimbursement = $splitBill.Expected
    } | Out-Null

    $goal = Invoke-Api -Method POST -Path 'api/SavingGoal' -Token $token -Body @{
        title        = $member.Goal.Title
        targetAmount = $member.Goal.Target
        savedAmount  = 0
        deadline     = $today.AddDays(180).ToString('yyyy-MM-dd')
        accountId    = $main.id
        categoryId   = (CategoryId 'Other' 0)
    }

    Invoke-Api -Method POST -Path "api/SavingContribution/$($goal.id)" -Token $token -Body @{
        amount    = $member.Goal.Contribution
        date      = (D 10)
        note      = 'Opening contribution'
        accountId = $main.id
    } | Out-Null

    $after = Get-ApiList -Path 'api/Account' -Token $token |
        Where-Object { $_.name -eq 'Main' } | Select-Object -First 1

    Write-Host "    seeded: 2 incomes, 6 expenses (1 split), 1 goal + contribution" -ForegroundColor DarkGray
    Write-Host "    balance $([decimal]$after.balance) EUR, reserved $([decimal]$after.reservedAmount) EUR" -ForegroundColor DarkGray

    $summary += [pscustomobject]@{
        Member   = $name
        Email    = $member.Email
        Expenses = 6
        Balance  = [decimal]$after.balance
        Reserved = [decimal]$after.reservedAmount
        Status   = 'seeded'
    }
}

Write-Host ""
$summary | Format-Table -AutoSize
Write-Host "  Password for every member: $Password" -ForegroundColor DarkGray
Write-Host "  Two-account browser pass: log in as alp@sim.local and mara@sim.local in two profiles." -ForegroundColor DarkGray
Write-Host ""

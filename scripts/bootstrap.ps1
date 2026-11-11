# Download a project-local, checksum-verified SDK; never changes PATH or the system installation.
. "$PSScriptRoot/common.ps1"
$version = '10.0.103'
$url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$version/dotnet-sdk-$version-win-x64.zip"
$sha512 = '519a011b2a41d50ff9a7ec6cdbe3927f19990c25efebb42860837c49953be904adac5bc1ff073ed80854a98ef9dbe54c00e2c9db84d84ba2dccd09f8e2261bf6'
$toolDir = Join-Path $script:ProjectRoot '.tools'
$sdkDir = Join-Path $toolDir 'dotnet'
$archive = Join-Path $toolDir 'dotnet-sdk.zip'
if (Test-Path -LiteralPath (Join-Path $sdkDir 'dotnet.exe')) {
    & (Join-Path $sdkDir 'dotnet.exe') --version
    exit $LASTEXITCODE
}
New-Item -ItemType Directory -Force $toolDir | Out-Null
Invoke-WebRequest -Uri $url -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $sha512) { throw 'SDK SHA512 校验失败，已停止。' }
Expand-Archive -LiteralPath $archive -DestinationPath $sdkDir -Force
& (Join-Path $sdkDir 'dotnet.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'SDK 解压后验证失败。' }
Remove-Item -LiteralPath $archive

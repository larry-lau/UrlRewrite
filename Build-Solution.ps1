param(
	[string]
	$MSBuildPath,
	[string]
	$Configuration = "Release",
	[string]
	$OutputPath = ".",
	$SolutionFile = "Hi.UrlRewrite.sln",
	[Switch]$SkipRestore,
	[Switch]$SkipBuild,
	[Switch]$SkipTest,
	[Switch]$SkipCopy
)

if ($PSVersionTable.PSVersion.Major -lt 5) {
	Write-Error "You must be running PowerShell 5.1. See https://www.microsoft.com/en-us/download/details.aspx?id=54616"
	exit
}

$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition

$OutputPath = (Get-Item $OutputPath).FullName
$WebsiteOutputPath = Join-Path $OutputPath _publish
$PackageOutputPath = Join-Path $OutputPath TdsGeneratedPackages

if (!$SkipRestore)
{
	nuget restore $SolutionFile
}

if (!$SkipBuild)
{
	if(-not $MSBuildPath) {
		$MSBuildPath = &"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe
	}
	
	if(Test-Path $OutputPath) {
		Remove-Item -Path $OutputPath -Recurse -Force
	}
	
	& $MSBuildPath $SolutionFile /p:Configuration=$Configuration /p:DeployOnBuild=True /p:DeployDefaultTarget=WebPublish /p:WebPublishMethod=FileSystem /p:PublishUrl=$WebsiteOutputPath /p:DebugSymbols=false /p:DebugType=None
}

if (!$SkipTest)
{
	$VSTestPath = &"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -property installationPath
	& "$VSTestPath\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" Hi.UrlRewrite.Tests\bin\Release\Hi.UrlRewrite.Tests.dll
}

if (!$SkipCopy)
{
	#xcopy /Y /I .\TdsGeneratedPackages\Package_Release $PackageOutputPath

	New-Item -ItemType Directory -Force -Path $WebsiteOutputPath\App_Data\items\core
	New-Item -ItemType Directory -Force -Path $WebsiteOutputPath\App_Data\items\master
	New-Item -ItemType Directory -Force -Path $WebsiteOutputPath\App_Data\items\web

	$ItemResourcesPath = "ItemResources_$Configuration"

	Get-ChildItem -Path . -Filter $ItemResourcesPath -Recurse | Get-ChildItem -Filter *.dat | % {
		$file = $_
		
		if ($file.Name.EndsWith('master.dat'))
		{
			$file.Name
			Copy-Item -Path $file.FullName  -Destination $WebsiteOutputPath\App_Data\items\master
		}
		
		if ($file.Name.EndsWith('core.dat'))
		{
			$file.Name
			Copy-Item -Path $file.FullName  -Destination $WebsiteOutputPath\App_Data\items\core
		}
	}
}

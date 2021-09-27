param(
	[string]
	$Configuration = "Release",
	[string]
	$OutputPath = "."
)

if ($PSVersionTable.PSVersion.Major -lt 5) {
	Write-Error "You must be running PowerShell 5.1. See https://www.microsoft.com/en-us/download/details.aspx?id=54616"
	exit
}

$OutputPath = (Get-Item $OutputPath).FullName
$WebsiteOutputPath = Join-Path $OutputPath _publish

New-Item -ItemType Directory -Force -Path $WebsiteOutputPath\App_Data\items\core  > $null
New-Item -ItemType Directory -Force -Path $WebsiteOutputPath\App_Data\items\master  > $null
New-Item -ItemType Directory -Force -Path $WebsiteOutputPath\App_Data\items\web  > $null

$ItemResourcesPath = "ItemResources_$Configuration"

Get-ChildItem -Path . -Filter $ItemResourcesPath -Recurse | Get-ChildItem -Filter *.dat | % {
	$file = $_
	
	if ($file.Name.EndsWith('master.dat'))
	{
		$destination = "$WebsiteOutputPath\App_Data\items\master"
		Write-Host "Copying $($file.Name) to $destination"
		Copy-Item -Path $file.FullName  -Destination $$destination > $null
	}
	
	if ($file.Name.EndsWith('core.dat'))
	{
		$destination = "$WebsiteOutputPath\App_Data\items\core"
		Write-Host "Copying $($file.Name) to $destination"
		Copy-Item -Path $file.FullName  -Destination $destination > $null
	}
}
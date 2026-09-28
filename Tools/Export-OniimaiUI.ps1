# Lossless extraction of the game's existing sprite regions for fast Android decoding.
# Outputs are committed with the source; running this is not required for normal builds.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path "$PSScriptRoot/..").Path
$source=Join-Path $root 'Assets/Sprites/Ribbon/newui@3x.png'
$target=Join-Path $root 'Assets/StreamingAssets/OniimaiUI'
$atlas=[System.Drawing.Bitmap]::FromFile($source)
try {
 $meta=Get-Content -LiteralPath ($source+'.meta') -Raw
 $regions=[regex]::Matches($meta,'name: newui@3x_(\d+)\s+rect:\s+serializedVersion: 2\s+x: (\d+)\s+y: (\d+)\s+width: (\d+)\s+height: (\d+)')
 foreach($region in $regions){
  $id=[int]$region.Groups[1].Value
  if($id -notin @(6,10,16,17,18,20,21,22,26)){continue}
  $x=[int]$region.Groups[2].Value;$y=[int]$region.Groups[3].Value;$w=[int]$region.Groups[4].Value;$h=[int]$region.Groups[5].Value
  $rect=[System.Drawing.Rectangle]::new($x,($atlas.Height-$y-$h),$w,$h)
  $sprite=$atlas.Clone($rect,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try{$sprite.Save((Join-Path $target "ui-$id.png"),[System.Drawing.Imaging.ImageFormat]::Png)}finally{$sprite.Dispose()}
 }
}finally{$atlas.Dispose()}

param([string]$A, [string]$B, [string]$Name)
Add-Type -AssemblyName System.Drawing

$ia=[System.Drawing.Bitmap]::FromFile($A); $ib=[System.Drawing.Bitmap]::FromFile($B)
$w=$ia.Width; $h=$ia.Height
$rect = New-Object System.Drawing.Rectangle 0,0,$w,$h
$ra=$ia.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$rb=$ib.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$n=$w*$h*4; $ba=New-Object byte[] $n; $bb=New-Object byte[] $n
[System.Runtime.InteropServices.Marshal]::Copy($ra.Scan0,$ba,0,$n)
[System.Runtime.InteropServices.Marshal]::Copy($rb.Scan0,$bb,0,$n)
$ia.UnlockBits($ra); $ib.UnlockBits($rb); $ia.Dispose(); $ib.Dispose()

# signed histogram per channel (B,G,R,A) of (cue4 - fmodel)
$hist = @{}
foreach($c in 0..3){ $hist[$c] = @{} }
for($i=0;$i -lt $n;$i+=4){
  foreach($c in 0..3){
    $d = [int]$ba[$i+$c] - [int]$bb[$i+$c]
    if($d -ne 0){ if($hist[$c].ContainsKey($d)){$hist[$c][$d]++} else {$hist[$c][$d]=1} }
  }
}
$names=@('B','G','R','A')
"=== $Name ($w x $h) : signed diff (cue4 - fmodel) ==="
foreach($c in 0..3){
  $tot = ($hist[$c].Values | Measure-Object -Sum).Sum
  if(-not $tot){ "  $($names[$c]): identical"; continue }
  $top = $hist[$c].GetEnumerator() | Sort-Object {[math]::Abs($_.Key)} | Select-Object -First 12
  "  $($names[$c]): $tot differing px -> " + (($top | ForEach-Object { "$($_.Key):$($_.Value)" }) -join '  ')
}

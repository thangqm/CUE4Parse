param([string]$Path, [string]$JsonOut)
$fs=[IO.File]::OpenRead($Path)
$br=New-Object IO.BinaryReader($fs)
$magic=$br.ReadUInt32(); $ver=$br.ReadUInt32(); $len=$br.ReadUInt32()
$chunks=@()
while($fs.Position -lt $len){
  $cl=$br.ReadUInt32(); $ct=$br.ReadUInt32()
  $data=$br.ReadBytes([int]$cl)
  $typeName = if($ct -eq 0x4E4F534A){"JSON"} elseif($ct -eq 0x004E4942){"BIN"} else {"0x{0:X}" -f $ct}
  $chunks += [pscustomobject]@{Type=$typeName; Length=$cl; Data=$data}
}
$fs.Close()
"file      : $Path"
"totalLen  : $len  (actual $((Get-Item $Path).Length))"
foreach($c in $chunks){ "chunk     : $($c.Type) len=$($c.Length)" }
$json = [Text.Encoding]::UTF8.GetString(($chunks | Where-Object Type -eq 'JSON').Data)
if($JsonOut){
  $obj = $json | ConvertFrom-Json
  $obj | ConvertTo-Json -Depth 100 | Set-Content -Path $JsonOut -Encoding utf8
}
$json.TrimEnd([char]0, ' ')

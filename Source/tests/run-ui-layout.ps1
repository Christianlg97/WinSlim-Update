param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../wumgr/bin/Release/WinSlimUpdate.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$windowType = $assembly.GetType('wumgr.WuMgr')
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$hitTest = $windowType.GetMethod('ResizeHitTest', $flags)
$size = [Drawing.Size]::new(1040,680)
foreach ($sample in @(@(1,1,13),@(1038,1,14),@(1,678,16),@(1038,678,17),@(1,200,10),@(1038,200,11),@(200,1,12),@(200,678,15),@(500,300,0),@(-1,200,0))) {
    $point = [Drawing.Point]::new($sample[0],$sample[1])
    $result = $hitTest.Invoke($null, @($size.PSObject.BaseObject,$point.PSObject.BaseObject,7))
    if ($result -ne $sample[2]) { throw "Incorrect resize edge at $point" }
}
$form = New-Object Windows.Forms.Form
$form.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
$form.ShowInTaskbar = $false
$form.Opacity = 0
$page = New-Object Windows.Forms.TableLayoutPanel
$page.Dock = [Windows.Forms.DockStyle]::Fill
$page.RowCount = 2
$page.RowStyles.Add([Windows.Forms.RowStyle]::new([Windows.Forms.SizeType]::Absolute,62)) | Out-Null
$page.RowStyles.Add([Windows.Forms.RowStyle]::new([Windows.Forms.SizeType]::Percent,100)) | Out-Null
$surface = New-Object Windows.Forms.Panel
$surface.Dock = [Windows.Forms.DockStyle]::Fill
$surface.Margin = [Windows.Forms.Padding]::new(28,0,28,10)
$surface.Padding = [Windows.Forms.Padding]::new(12,8,12,8)
$actions = New-Object Windows.Forms.FlowLayoutPanel
$actions.Dock = [Windows.Forms.DockStyle]::Fill
$actions.Margin = [Windows.Forms.Padding]::Empty
foreach ($width in @(140,180,150,315)) {
    $button = New-Object Windows.Forms.Button
    $button.Size = [Drawing.Size]::new($width,36)
    $button.Margin = [Windows.Forms.Padding]::new(0,0,8,0)
    $actions.Controls.Add($button)
}
$surface.Controls.Add($actions)
$page.Controls.Add($surface,0,0)
$form.Controls.Add($page)
$windowType.GetMethod('ConfigureResponsiveActions',$flags).Invoke($null,@($surface.PSObject.BaseObject,$actions.PSObject.BaseObject))
try {
    $form.Show()
    foreach ($width in @(804,944,1366,804)) {
        $form.ClientSize = [Drawing.Size]::new($width,500)
        $form.PerformLayout()
        $actions.PerformLayout()
        [Windows.Forms.Application]::DoEvents()
        foreach ($button in $actions.Controls) {
            if ($button.Right -gt $actions.ClientSize.Width -or $button.Bottom -gt $actions.ClientSize.Height) {
                throw "Clipped action at width $width : $($button.Bounds), viewport $($actions.ClientSize)"
            }
        }
        Write-Output "PASS: action layout at content width $width, row height $($page.RowStyles[0].Height)"
    }
    $expanded = $page.RowStyles[0].Height
    $actions.Controls[1].Visible = $false
    $actions.PerformLayout()
    [Windows.Forms.Application]::DoEvents()
    if ($page.RowStyles[0].Height -ge $expanded) { throw 'Action row did not shrink after hiding an action' }
    $page.RowStyles[0].Height = 230
    $page.PerformLayout()
    [Windows.Forms.Application]::DoEvents()
    if ($page.RowStyles[0].Height -ne 62) { throw 'Initial oversized banner was not corrected' }
    $actions.Visible = $false
    $page.PerformLayout()
    [Windows.Forms.Application]::DoEvents()
    if ($page.RowStyles[0].Height -ne 62) { throw 'Hidden actions retained an oversized banner' }
    Write-Output 'PASS: action visibility restores one-row layout; resize corners and edges verified'
} finally {
    $form.Dispose()
}

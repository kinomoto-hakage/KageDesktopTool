using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace Kage.Desktop;

internal static class ContentScrollStyle
{
    internal static void Apply(ListBox items)
    {
        // 保留标准 Track 与滚动命令；轨道和滑块仍有 14 DIP 的命中宽度。
        items.Resources[typeof(ScrollBar)] = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Setter Property="Width" Value="14"/>
              <Setter Property="MinWidth" Value="14"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Border Background="#18000000" CornerRadius="7" Margin="1,0">
                      <Track x:Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True"
                             Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                             Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False">
                            <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template>
                          </RepeatButton>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb MinHeight="24" Margin="2,1" Background="#B8FFFFFF">
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="5"/></ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False">
                            <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template>
                          </RepeatButton>
                        </Track.IncreaseRepeatButton>
                      </Track>
                    </Border>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """);
    }
}

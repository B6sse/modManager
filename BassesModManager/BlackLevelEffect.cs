using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BassesModManager
{
    /// <summary>
    /// Pulls near-black down to true black. Used on the background video, whose encoded
    /// black is a dark grey; with the lift at the clip's black level the window reads as
    /// black with the picture on it. The pixel shader is Shaders\BlackLevel.fx, compiled
    /// to BlackLevel.ps and embedded as a resource.
    /// </summary>
    public class BlackLevelEffect : ShaderEffect
    {
        private static readonly PixelShader Shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/BassesModManager;component/Shaders/BlackLevel.ps")
        };

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(BlackLevelEffect), 0);

        /// <summary>The level (0 to 1) that becomes black; 16/255 is about 0.063.</summary>
        public static readonly DependencyProperty LiftProperty =
            DependencyProperty.Register("Lift", typeof(double), typeof(BlackLevelEffect),
                new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

        public BlackLevelEffect()
        {
            PixelShader = Shader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(LiftProperty);
        }

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }

        public double Lift
        {
            get => (double)GetValue(LiftProperty);
            set => SetValue(LiftProperty, value);
        }
    }
}

using System;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using EDIDParser;
using EDIDParser.Descriptors;
using EDIDParser.Enums;
using NvAPIWrapper.Display;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native.Display;

namespace novideo_srgb
{
    public class MonitorData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly GPUOutput _output;
        private bool _clamped;
        private int _bitDepth;
        private Novideo.DitherControl _dither;

        private MainViewModel _viewModel;

        public MonitorData(MainViewModel viewModel, int number, Display display, string path, bool hdrActive, bool clampSdr)
        {
            _viewModel = viewModel;
            Number = number;
            _output = display.Output;

            _bitDepth = 0;
            try
            {
                var bitDepth = display.DisplayDevice.CurrentColorData.ColorDepth;
                if (bitDepth == ColorDataDepth.BPC6)
                    _bitDepth = 6;
                else if (bitDepth == ColorDataDepth.BPC8)
                    _bitDepth = 8;
                else if (bitDepth == ColorDataDepth.BPC10)
                    _bitDepth = 10;
                else if (bitDepth == ColorDataDepth.BPC12)
                    _bitDepth = 12;
                else if (bitDepth == ColorDataDepth.BPC16)
                    _bitDepth = 16;
            }
            catch (Exception)
            {
            }


            Edid = Novideo.GetEDID(path, display);

            // Checking for null since current code uses NVAPI to retrieve EDID which does not
            // seem to be supported on OSes limited to .net40
            // Bypassing until I can confirm byte math with registry on test machine

            if (Edid != null)
            {
                var descriptor = Edid.Descriptors.OfType<StringDescriptor>()
                                .FirstOrDefault(x => x.Type == StringDescriptorType.MonitorName);
                if (descriptor != null)
                    Name = descriptor.Value;
                else
                    Name = "<no name>";
            }
            else
                Name = "<no name>";

            Path = path;
            ClampSdr = clampSdr;
            HdrActive = hdrActive;

            // bypassing until can confirm edidparser math on test machine
            if (Edid != null)
            {
                var coords = Edid.DisplayParameters.ChromaticityCoordinates;

                if (coords != null)
                {
                    EdidColorSpace = new Colorimetry.ColorSpace
                    {
                        Red = new Colorimetry.Point { X = Math.Round(coords.RedX, 3), Y = Math.Round(coords.RedY, 3) },
                        Green = new Colorimetry.Point { X = Math.Round(coords.GreenX, 3), Y = Math.Round(coords.GreenY, 3) },
                        Blue = new Colorimetry.Point { X = Math.Round(coords.BlueX, 3), Y = Math.Round(coords.BlueY, 3) },
                        White = Colorimetry.D65
                    };
                }
                // Hard coding to sRGB primaries if EDID fails
                else
                {
                    EdidColorSpace = new Colorimetry.ColorSpace
                    {
                        Red = new Colorimetry.Point { X = Math.Round(0.6400, 3), Y = Math.Round(0.3300, 3) },
                        Green = new Colorimetry.Point { X = Math.Round(0.3000, 3), Y = Math.Round(0.6000, 3) },
                        Blue = new Colorimetry.Point { X = Math.Round(0.1500, 3), Y = Math.Round(0.0600, 3) },
                        White = Colorimetry.D65
                    };
                }
            }

            // Appears to be unsupported nvapi function on OSes limited to .net40, skipping for now
            //_dither = Novideo.GetDitherControl(_output);
            // force dither
            _dither = new Novideo.DitherControl();
            _dither.state = 2;

            // Appears to be unsupported nvapi function on OSes limited to .net40, skipping for now
            //_clamped = Novideo.IsColorSpaceConversionActive(_output);
            _clamped = false;

            ProfilePath = "";
            CustomGamma = 2.2;
            CustomPercentage = 100;
        }

        public MonitorData(MainViewModel viewModel, int number, Display display, string path, bool hdrActive, bool clampSdr, bool useIcc, string profilePath,
            bool calibrateGamma,
            int selectedGamma, double customGamma, double customPercentage, int target, bool disableOptimization) :
            this(viewModel, number, display, path, hdrActive, clampSdr)
        {
            UseIcc = useIcc;
            ProfilePath = profilePath;
            CalibrateGamma = calibrateGamma;
            SelectedGamma = selectedGamma;
            CustomGamma = customGamma;
            CustomPercentage = customPercentage;
            Target = target;
            DisableOptimization = disableOptimization;
        }

        public int Number { get; }
        public string Name { get; }
        public EDID Edid { get; }
        public string Path { get; }
        public bool ClampSdr { get; set; }
        public bool HdrActive { get; }

        private void UpdateClamp(bool doClamp)
        {
            if (_clamped)
            {
                Novideo.DisableColorSpaceConversion(_output);
            }

            if (!doClamp) return;

            if (_clamped) Thread.Sleep(100);
            if (UseEdid)
                Novideo.SetColorSpaceConversion(_output, Colorimetry.RGBToRGB(TargetColorSpace, EdidColorSpace));
            else if (UseIcc)
            {
                var profile = ICCMatrixProfile.FromFile(ProfilePath);
                if (CalibrateGamma)
                {
                    var trcBlack = Matrix.FromValues(new[,]
                    {
                        { profile.trcs[0].SampleAt(0) },
                        { profile.trcs[1].SampleAt(0) },
                        { profile.trcs[2].SampleAt(0) }
                    });
                    var black = (profile.matrix * trcBlack)[1];

                    ToneCurve gamma;
                    switch (SelectedGamma)
                    {
                        case 0:
                            gamma = new SrgbEOTF(black);
                            break;
                        case 1:
                            gamma = new GammaToneCurve(2.4, black, 0);
                            break;
                        case 2:
                            gamma = new GammaToneCurve(CustomGamma, black, CustomPercentage / 100);
                            break;
                        case 3:
                            gamma = new GammaToneCurve(CustomGamma, black, CustomPercentage / 100, true);
                            break;
                        case 4:
                            gamma = new LstarEOTF(black);
                            break;
                        default:
                            throw new NotSupportedException("Unsupported gamma type " + SelectedGamma);
                    }

                    Novideo.SetColorSpaceConversion(_output, profile, TargetColorSpace, gamma, DisableOptimization);
                }
                else
                {
                    Novideo.SetColorSpaceConversion(_output, profile, TargetColorSpace);
                }
            }
        }

        private void HandleClampException(Exception e)
        {
            MessageBox.Show(e.Message);
            _clamped = Novideo.IsColorSpaceConversionActive(_output);
            ClampSdr = _clamped;
            _viewModel.SaveConfig();
            OnPropertyChanged(nameof(Clamped));
        }
        
        public bool Clamped
        {
            set
            {
                try
                {
                    UpdateClamp(value);
                    ClampSdr = value;
                    _viewModel.SaveConfig();
                }
                catch (Exception e)
                {
                    HandleClampException(e);
                    return;
                }

                _clamped = value;
                OnPropertyChanged();
            }
            get => _clamped;
        }

        public void ReapplyClamp()
        {
            try
            {
                var clamped = CanClamp && ClampSdr;
                UpdateClamp(clamped);
                _clamped = clamped;
                OnPropertyChanged(nameof(CanClamp));
            }
            catch (Exception e)
            {
                HandleClampException(e);
            }
        }

        public bool CanClamp => !HdrActive && (UseEdid && !EdidColorSpace.Equals(TargetColorSpace) || UseIcc && ProfilePath != "");

        public string GPU => _output.PhysicalGPU.FullName;

        public bool UseEdid
        {
            set => UseIcc = !value;
            get => !UseIcc;
        }

        public bool UseIcc { set; get; }

        public string ProfilePath { set; get; }

        public bool CalibrateGamma { set; get; }

        public int SelectedGamma { set; get; }

        public double CustomGamma { set; get; }

        public double CustomPercentage { set; get; }

        public bool DisableOptimization { set; get; }

        public int Target { set; get; }

        public Colorimetry.ColorSpace EdidColorSpace { get; }

        private Colorimetry.ColorSpace TargetColorSpace => Colorimetry.ColorSpaces[Target];

        public Novideo.DitherControl DitherControl => _dither;

        public string DitherString
        {
            get
            {
                string[] types =
                {
                    "SpatialDynamic",
                    "SpatialStatic",
                    "SpatialDynamic2x2",
                    "SpatialStatic2x2",
                    "Temporal"
                };
                if (_dither.state == 2)
                {
                    return "Disabled (forced)";
                }
                if (_dither.state == 0 & _dither.bits == 0 && _dither.mode == 0)
                {
                    return "Disabled (default)";
                }
                var bits = (6 + 2 * _dither.bits).ToString();
                return bits + " bit " + types[_dither.mode] + " (" + (_dither.state == 0 ? "default" : "forced") + ")";
            }
        }

        public int BitDepth => _bitDepth;

        public void ApplyDither(int state, int bits, int mode)
        {
            try
            {
                Novideo.SetDitherControl(_output, state, bits, mode);
                _dither = Novideo.GetDitherControl(_output);
                OnPropertyChanged(nameof(DitherString));
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
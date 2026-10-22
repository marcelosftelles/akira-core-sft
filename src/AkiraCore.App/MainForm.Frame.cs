using System.Runtime.InteropServices;
using AkiraCore.App.Web;

namespace AkiraCore.App;

/// <summary>
/// Barra de título própria: a janela não tem a faixa do Windows em cima e a página desenha a
/// dela (área de arrastar com app-region e os botões minimizar, maximizar e fechar).
/// As bordas de redimensionar, a sombra, os cantos e o Aero Snap continuam sendo do Windows.
/// Se o WebView2 não abrir ou não suportar app-region, a janela volta à barra nativa.
/// </summary>
public sealed partial class MainForm
{
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmSysCommand = 0x0112;
    private const int HtClient = 1;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int ScMinimize = 0xF020;
    private const int ScMaximize = 0xF030;
    private const int ScClose = 0xF060;
    private const int ScRestore = 0xF120;
    private const int SmCySizeFrame = 33;
    private const int SmCxPaddedBorder = 92;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    // Mesmas medidas do app.css: largura da barra lateral e da janela estreita (lateral em cima)
    private const int SidebarCssWidth = 236;
    private const int NarrowCssWidth = 760;

    private bool _customFrame = true;
    private bool _active;
    private bool _maximized;

    /// <summary>Trata as mensagens da moldura; true quando a mensagem já foi respondida.</summary>
    private bool HandleFrameMessage(ref Message m)
    {
        if (!_customFrame)
        {
            return false;
        }
        if (m.Msg == WmNcCalcSize && m.WParam != IntPtr.Zero)
        {
            // Primeiro retângulo do NCCALCSIZE_PARAMS: left, top, right, bottom
            var top = Marshal.ReadInt32(m.LParam, 4);
            var bottom = Marshal.ReadInt32(m.LParam, 12);
            base.WndProc(ref m);
            // O Windows desconta as bordas; em cima a área do app vai até o topo, sem barra.
            // Maximizada, a janela passa da tela pela espessura da borda, então desconta em cima também
            var frame = bottom - Marshal.ReadInt32(m.LParam, 12);
            Marshal.WriteInt32(m.LParam, 4, IsZoomed(Handle) ? top + frame : top);
            m.Result = IntPtr.Zero;
            return true;
        }
        if (m.Msg == WmNcHitTest)
        {
            base.WndProc(ref m);
            // A faixa fina acima da página (ver UpdateFrame) redimensiona pela borda de cima
            if ((int)m.Result == HtClient && !IsZoomed(Handle))
            {
                var lParam = (int)(long)m.LParam;
                var point = PointToClient(new Point((short)(lParam & 0xFFFF), (short)(lParam >> 16)));
                var band = ResizeBand();
                if (point.Y < band)
                {
                    m.Result = point.X < band ? HtTopLeft : point.X >= ClientSize.Width - band ? HtTopRight : HtTop;
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>Espessura da borda de redimensionar no DPI do monitor atual.</summary>
    private int ResizeBand() =>
        GetSystemMetricsForDpi(SmCySizeFrame, (uint)DeviceDpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, (uint)DeviceDpi);

    /// <summary>
    /// A página cobre a janela toda; só uma faixa da altura da borda fica de fora em cima,
    /// para o Windows achar a borda de redimensionar. Maximizada não há borda nem faixa.
    /// </summary>
    private void UpdateFrame()
    {
        if (!IsHandleCreated)
        {
            return;
        }
        var maximized = IsZoomed(Handle);
        var band = _customFrame && !maximized ? ResizeBand() : 0;
        if (Padding.Top != band)
        {
            Padding = new Padding(0, band, 0, 0);
        }
        Invalidate(new Rectangle(0, 0, ClientSize.Width, band));
        if (maximized != _maximized)
        {
            _maximized = maximized;
            PostWindowState();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var band = Padding.Top;
        if (band == 0)
        {
            return;
        }
        // A faixa continua a página: cor da barra lateral à esquerda e do painel à direita
        var scale = DeviceDpi / 96f;
        var width = ClientSize.Width / scale <= NarrowCssWidth
            ? ClientSize.Width
            : (int)Math.Round(SidebarCssWidth * scale);
        using var chrome = new SolidBrush(WindowChrome.Chrome(_darkTheme));
        e.Graphics.FillRectangle(chrome, 0, 0, width, band);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateFrame();
    }

    /// <summary>Volta à barra de título do Windows (sem WebView2 ou sem app-region).</summary>
    private void UseNativeFrame()
    {
        if (!_customFrame)
        {
            return;
        }
        _customFrame = false;
        UpdateFrame();
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
        PostWindowState();
    }

    /// <summary>Botões da barra de título da página: o mesmo comando que os botões nativos mandam.</summary>
    private void HandleWindowCommand(string? action)
    {
        var command = action switch
        {
            "minimize" => ScMinimize,
            "maximize" => IsZoomed(Handle) ? ScRestore : ScMaximize,
            "close" => ScClose, // fecha para a bandeja, como o X nativo (OnFormClosing)
            _ => 0,
        };
        if (command != 0)
        {
            PostMessage(Handle, WmSysCommand, command, IntPtr.Zero);
        }
    }

    private void SetActive(bool active)
    {
        _active = active;
        PostWindowState();
    }

    private void PostWindowState() =>
        Post(new { type = "window", custom = _customFrame, maximized = _maximized, active = _active });

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int message, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

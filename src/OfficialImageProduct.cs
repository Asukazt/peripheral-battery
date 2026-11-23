#nullable disable
using System;

namespace PeriDock;

internal sealed record OfficialImageProduct(string[] Models, Uri Page, OfficialImageColor[] Colors);

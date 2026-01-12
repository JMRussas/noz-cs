//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public class Asset {
    public string Name { get; private set; }

    protected Asset(string name)
    {
        Name = name;
    }
}

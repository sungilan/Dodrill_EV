using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;

namespace DoDrill
{
    public class Panel_LanguageToggle : UIView
    {
        protected override void Awake()
        {
            base.Awake();
            Mst.Events.AddListener(MstEventKeys.showLanguageToggle, (_) => Show());
            Mst.Events.AddListener(MstEventKeys.hideLanguageToggle, (_) => Hide());
        }
    }
}
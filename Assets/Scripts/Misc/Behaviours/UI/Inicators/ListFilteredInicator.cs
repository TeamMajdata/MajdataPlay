using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;

namespace MajdataPlay.Behaviours.UI.Inicators
{
    public class ListFilteredInicator : UIInicator
    {
        public TextMeshProUGUI SearchText;
        protected override void Awake()
        {
            base.Awake();
            var actionTo = ActionTo;
            if (actionTo == null)
            {
                actionTo = gameObject;
            }
            var keyword = MajEnv.RuntimeConfig.List.OrderBy.Keyword;
            if (!string.IsNullOrEmpty(keyword))
            {
                SearchText.text = "<#FF8282>🔍<#4F5763> " + keyword;
                actionTo.SetActive(true);
            }
            else
            {
                actionTo.SetActive(false);
            }
        }
    }
}

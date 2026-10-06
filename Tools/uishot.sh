#!/usr/bin/env bash
# Click a UI Toolkit button by name (NavigationSubmit) then capture: uishot.sh <button> <capname>
cd "$(dirname "$0")/.."
Tools/u.sh eval "var b = Salvo.UIRoot.I.Q<UnityEngine.UIElements.Button>(\"$1\"); if (b == null) return \"no button\"; using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); } return \"ok\";" >/dev/null
sleep ${3:-2}
Tools/cap.sh "$2" >/dev/null

var patt=new RegExp("[0-9]{2,15}");
var id = patt.exec(document.URL)[0];
var patt2=new RegExp("app: ([0-9]*)");
var patt3=new RegExp("\\?appid=([0-9]*)");

var app = patt3.exec(document.body.innerHTML)[1];




var realButton = document.getElementById("SubscribeItemBtn");

// shorten the text in the box because it will be in the way
realButton.parentNode.parentNode.getElementsByTagName("h1")[0].innerHTML = "Download/Subscribe to the right";

var myButtonPosition = realButton.offsetWidth + 20;

var button = document.createElement('div');

button.innerHTML = `
<input type="button" style="pointer-events:none;" class="btn_medium  btn_border_2px" value="connecting..." id="steamdownload1">
<div id="result"></div>
`

// append the element after the real subscribe button
if (realButton.nextSibling)
{
    realButton.parentNode.insertBefore(button, realButton.nextSibling);
}
else
{
    realButton.parentNode.appendChild(button);
}






$(document).ready(function()
{
	data =  {
					ItemId: id,
					AppId: app,
					ItemName: $(".workshopItemTitle")[0].innerHTML,
					ActionID: 0
				}
				
	function openWebSocket() {
		var ws = new WebSocket("ws://localhost:1024/");
			
		ws.onopen = function (event) {
			$("#steamdownload1").attr("style", "pointer-events:none;")
			$("#steamdownload1").attr("value", "please wait... analizing......")
			$("#steamdownload1").attr("class", "btn_medium  btn_border_2px")
			data.ActionID	= 1	
			ws.send(JSON.stringify(data))
		}

		ws.onmessage = function (event) {
		  if (event.data == "yes") {
			  $("#steamdownload1").attr("style", "")
			  $("#steamdownload1").attr("value", "Download from SteamCmd")
			  $("#steamdownload1").attr("class", "btn_medium  btn_border_2px btn_green_white_innerfade")
		  }
		  if (event.data == "no") {
			  $("#steamdownload1").attr("value", "Game is not availible")
			  $("#steamdownload1").attr("style", "pointer-events:none;")
		  }
		}
		
		ws.onclose = function () {
			$("#steamdownload1").attr("style", "pointer-events:none;")
			$("#steamdownload1").attr("value", "connecting...")
			$("#steamdownload1").attr("class", "btn_medium  btn_border_2px")
		  setTimeout(function() {
					openWebSocket()
			}, 100)
		}
		
		$("#steamdownload1").click(function()
		{
			data.ActionID = 0
			ws.send(JSON.stringify(data))
		});
	}
	
	openWebSocket()
})
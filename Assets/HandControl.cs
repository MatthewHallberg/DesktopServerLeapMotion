using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.Net;
using System.Net.Sockets;
using System.Text;

public class HandControl : MonoBehaviour {

	private static int localPort;

	private string IP = "10.0.0.37"; //cell

	private int port = 1999;  

	IPEndPoint remoteEndPoint;
	UdpClient client;

	private string strMessage;

	public static Vector3 offSetLeft = Vector3.zero;
	public static Vector3 offSetRight = Vector3.zero;

	// Use this for initialization
	void Start () {

		Application.runInBackground = true;

		remoteEndPoint = new IPEndPoint(IPAddress.Parse(IP), port);
		client = new UdpClient();

		//load saved offset data
		if (PlayerPrefs.HasKey ("offsetLeftX")) {
			offSetLeft = new Vector3(PlayerPrefs.GetFloat("offsetLeftX"),PlayerPrefs.GetFloat("offsetLeftY"),PlayerPrefs.GetFloat("offsetLeftZ"));
		}
		if (PlayerPrefs.HasKey ("offsetRightX")) {
			offSetRight = new Vector3(PlayerPrefs.GetFloat("offsetRightX"),PlayerPrefs.GetFloat("offsetRightY"),PlayerPrefs.GetFloat("offsetRightZ"));
		}

		StartCoroutine (SendData ());
	}

	void OnApplicationQuit()
	{
		//save offset x
		PlayerPrefs.SetFloat ("offsetLeftX", offSetLeft.x);
		PlayerPrefs.SetFloat ("offsetLeftY", offSetLeft.y);
		PlayerPrefs.SetFloat ("offsetLeftZ", offSetLeft.z);

		//save offset y
		PlayerPrefs.SetFloat ("offsetRightX", offSetRight.x);
		PlayerPrefs.SetFloat ("offsetRightY", offSetRight.y);
		PlayerPrefs.SetFloat ("offsetRightZ", offSetRight.z);
	}

	IEnumerator SendData(){

		while (true) {

			if (transform.childCount > 0) {
				
				strMessage = "";

				if (transform.Find ("Left(Clone)") != null) {
					Transform leftHand = transform.Find ("Left(Clone)").transform.GetChild(1);

					Vector3 leftHandPosition = leftHand.position + offSetLeft;
					//add comma so we can split by string when we recieve
					strMessage += "l," + leftHandPosition.ToString() + "," + leftHand.rotation.ToString() + ",";
				}
				if (transform.Find ("Right(Clone)") != null) {
					Transform rightHand = transform.Find ("Right(Clone)").transform.GetChild(1);

					Vector3 rightHandPosition = rightHand.position + offSetRight;
					strMessage += "r," + rightHandPosition.ToString() + "," + rightHand.rotation.ToString() + ",";
				}

			} else {
				//clear out message every iteration
				strMessage = "nothing";
			}	

			byte[] data = Encoding.UTF8.GetBytes (strMessage);

			var message = client.Send (data, data.Length, remoteEndPoint);
			yield return message;
		}
	}
 }

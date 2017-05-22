using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.Net;
using System.Net.Sockets;
using System.Text;

public class HandControl : MonoBehaviour {

	private static int localPort;

	//private string IP = "10.0.0.37"; //cell
	private string IP = "127.0.0.1"; //home

	private int port = 1999;  

	IPEndPoint remoteEndPoint;
	UdpClient client;

	private string strMessage;

	// Use this for initialization
	void Start () {

		remoteEndPoint = new IPEndPoint(IPAddress.Parse(IP), port);
		client = new UdpClient();

		StartCoroutine (SendData ());
	}

	IEnumerator SendData(){

		while (true) {

			if (transform.childCount > 0) {
				
				strMessage = "";

				if (transform.Find ("Left(Clone)") != null) {
					Transform leftHand = transform.Find ("Left(Clone)").transform.GetChild(1);
					//add comma so we can split by string when we recieve
					strMessage += "l," + leftHand.position.ToString() + "," + leftHand.rotation.ToString() + ",";
				}
				if (transform.Find ("Right(Clone)") != null) {
					Transform rightHand = transform.Find ("Right(Clone)").transform.GetChild(1);
					strMessage += "r," + rightHand.position.ToString() + "," + rightHand.rotation.ToString() + ",";
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

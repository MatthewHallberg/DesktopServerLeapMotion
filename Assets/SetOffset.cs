using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SetOffset : MonoBehaviour {

	public void IncrementLeftX(){
		HandControl.offSetLeft.x += .1f;
	}

	public void DecrementLeftX(){
		HandControl.offSetLeft.x -= .1f;
	}
	public void IncrementLeftY(){
		HandControl.offSetLeft.y += .1f;
	}

	public void DecrementLeftY(){
		HandControl.offSetLeft.y -= .1f;
	}
	public void IncrementLeftZ(){
		HandControl.offSetLeft.z += .1f;
	}

	public void DecrementLeftZ(){
		HandControl.offSetLeft.z -= .1f;
	}
	public void IncrementRightX(){
		HandControl.offSetRight.x += .1f;
	}

	public void DecrementRightX(){
		HandControl.offSetRight.x -= .1f;
	}
	public void IncrementRightY(){
		HandControl.offSetRight.y += .1f;
	}

	public void DecrementRightY(){
		HandControl.offSetRight.y -= .1f;
	}
	public void IncrementRightZ(){
		HandControl.offSetRight.z += .1f;
	}

	public void DecrementRightZ(){
		HandControl.offSetRight.z -= .1f;
	}
}

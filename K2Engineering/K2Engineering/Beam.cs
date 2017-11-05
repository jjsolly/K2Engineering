﻿using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using KangarooSolver;

namespace K2Engineering
{
    public class Beam : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Beam class.
        /// </summary>
        public Beam()
          : base("Beam", "Beam",
              "A goal that represents a beam element with biaxial bending and torsion behaviour",
              "K2Eng", "0 Elements")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("StartPlane", "startPln", "The start plane of the beam (model in [m])", GH_ParamAccess.item);
            pManager.AddPlaneParameter("EndPlane", "endPln", "The end plane of the beam (model in [m])", GH_ParamAccess.item);
            pManager.AddNumberParameter("E-modulus", "E", "Young's modulus in [MPa]", GH_ParamAccess.item);
            pManager.AddNumberParameter("G-modulus", "G", "The shear modulus in [MPa]", GH_ParamAccess.item);
            pManager.AddNumberParameter("A", "A", "The cross section area in [mm2]", GH_ParamAccess.item);
            pManager.AddNumberParameter("Iy", "Iy", "The moment of inertia about the cross section y-axis in [mm4]", GH_ParamAccess.item);
            pManager.AddNumberParameter("Iz", "Iz", "The moment of inertia about the cross section z-axis in [mm4]", GH_ParamAccess.item);
            pManager.AddNumberParameter("It", "It", "The torsional moment of inertia in [mm4]", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "Beam", "The beam goal", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //Input
            Plane startPln = new Plane();
            DA.GetData(0, ref startPln);

            Plane endPln = new Plane();
            DA.GetData(1, ref endPln);

            double eModulus = 0.0;
            DA.GetData(2, ref eModulus);

            double gModulus = 0.0;
            DA.GetData(3, ref gModulus);

            double area = 0.0;
            DA.GetData(4, ref area);

            double inertiaY = 0.0;
            DA.GetData(5, ref inertiaY);

            double inertiaZ = 0.0;
            DA.GetData(6, ref inertiaZ);

            double inertiaT = 0.0;
            DA.GetData(7, ref inertiaT);


            //Calculate
            GoalObject beamElement = new BeamGoal(startPln, endPln, eModulus, gModulus, area, inertiaY, inertiaZ, inertiaT);


            //Output
            DA.SetData(0, beamElement);
        }

        //Define beam goal
        public class BeamGoal : GoalObject
        {
            Plane P0;               //Start plane (local)
            Plane P1;               //End plane (local)
            Plane P0R;              //Updated start plane (local)
            Plane P1R;              //Updated end plane (local)

            double restLength;
            double ext;     //check
            double extSimple;       //check
            Vector3d axialMove;     //check

            double E, G, A, Iy, Iz, It;
            double thetaY0, thetaZ0, thetaY1, thetaZ1, thetaX;

            double N, MY0, MZ0, MY1, MZ1, MX;


            public BeamGoal(Plane startPlane, Plane endPlane, double eModulus, double gModulus, double area, double inertiaY, double inertiaZ, double inertiaT)
            {
                PPos = new Point3d[2] {startPlane.Origin, endPlane.Origin};
                Move = new Vector3d[2];
                Weighting = new double[2] {1.0, 1.0};           

                Torque = new Vector3d[2];
                TorqueWeighting = new double[2] {1.0, 1.0};

                Plane startGlobal = new Plane(startPlane.Origin, Vector3d.XAxis, Vector3d.YAxis);
                Plane endGlobal = new Plane(endPlane.Origin, Vector3d.XAxis, Vector3d.YAxis);
                InitialOrientation = new Plane[2] { startGlobal, endGlobal };       //Needs to be global because the calculated forces and rotations in nodes are calculated globally

                //Local end planes
                P0 = startPlane;
                P1 = endPlane;
                P0R = P0;
                P1R = P1;

                //Translate initial local end planes to Origin
                P0.Transform(Transform.ChangeBasis(Plane.WorldXY, startGlobal));
                P1.Transform(Transform.ChangeBasis(Plane.WorldXY, endGlobal));

                restLength = startPlane.Origin.DistanceTo(endPlane.Origin);
                E = eModulus;
                G = gModulus;
                A = area;
                Iy = inertiaY;
                Iz = inertiaZ;
                It = inertiaT;
            }


            public override void Calculate(List<KangarooSolver.Particle> p)
            {
                //Get the current positions/orientations of the nodes (global: related to InitialOrientation)
                Plane P0Current = p[PIndex[0]].Orientation;
                Plane P1Current = p[PIndex[1]].Orientation;

                Vector3d elementVec = new Vector3d(P1Current.Origin - P0Current.Origin);
                double currentLength = elementVec.Length;
                Vector3d elementDir = new Vector3d(elementVec);
                elementDir.Unitize();


                //Calculate angle changes (referring to Eq. 2.1.7 to 2.1.9)

                //Get the initial orientations of the end planes (local orientation sitting in Origin)
                P0R = P0;
                P1R = P1;

                //Orient initial local plane sitting in Origin from WorldXY to current global plane in node. This gives the updated local plane
                P0R.Transform(Transform.PlaneToPlane(Plane.WorldXY, P0Current));
                P1R.Transform(Transform.PlaneToPlane(Plane.WorldXY, P1Current));

                //Extract vectors of updated local planes
                Vector3d Y0 = P0R.XAxis;
                Vector3d Z0 = P0R.YAxis;
                Vector3d Y1 = P1R.XAxis;
                Vector3d Z1 = P1R.YAxis;

                //Bending angle changes around local axes
                thetaY0 = Z0 * elementDir;
                thetaZ0 = Y0 * elementDir;         //Should this be negative or is it accounted for in the following equations? Emil uses (-) whereas Olsson and Piker use (+)
                thetaY1 = Z1 * elementDir;
                thetaZ1 = Y1 * elementDir;         //Should this be negative?

                //Twist angle change around element axis
                thetaX = ((Y0 * Z1) - (Y1 * Z0)) / 2.0;


                //Axial
                //double extension = ((Math.Pow(currentLength, 2) - Math.Pow(restLength, 2)) / (2.0 * restLength)) + ((restLength / 60.0) * (4.0 * (Math.Pow(thetaY0, 2) + Math.Pow(thetaZ0, 2)) - 2.0 * ((thetaY0 * thetaY1) - (thetaZ0 * thetaZ1)) + 4.0 * (Math.Pow(thetaY1, 2) + Math.Pow(thetaZ1, 2))));         //Unit: [m]
                double extension = (Math.Pow(currentLength, 2) - Math.Pow(restLength, 2)) / (2.0 * restLength);     //Try simple axial without bowing to start with

                ext = extension;    //check
                extSimple = currentLength - restLength;     //check


                //Element internal forces (referring to 2.1.11 to 2.1.16)
                N = ((E * A) / restLength) * extension;      // Unit: [N]

                MY0 = (((N * restLength) / 30.0) * ((4.0 * thetaY0) - thetaY1)) + (((2.0 * E * Iy * 1e-6) / restLength) * ((2.0 * thetaY0) - thetaY1));          //Unit: [Nm]
                MY1 = (((N * restLength) / 30.0) * ((4.0 * thetaY1) - thetaY0)) + (((2.0 * E * Iy * 1e-6) / restLength) * ((2.0 * thetaY1) - thetaY0));          //Unit: [Nm]

                MZ0 = (((N * restLength) / 30.0) * ((4.0 * thetaZ0) - thetaZ1)) + (((2.0 * E * Iz * 1e-6) / restLength) * ((2.0 * thetaZ0) - thetaZ1));          //Unit: [Nm]
                MZ1 = (((N * restLength) / 30.0) * ((4.0 * thetaZ1) - thetaZ0)) + (((2.0 * E * Iz * 1e-6) / restLength) * ((2.0 * thetaZ1) - thetaZ0));          //Unit: [Nm]

                MX = ((G * It * 1e-6) / restLength) * thetaX;            //Unit: [Nm]


                //To do: calculate shear forces from moments


                //Global forces (referring to 2.1.17 to 2.1.20)

                //Force start
                double F0X = (1.0 / restLength) * ((N * elementDir.X) + (MY0 * Z0.X) - (MZ0 * Y0.X) + (MY1 * Z1.X) - (MZ1 * Y1.X));     //Last term (-) according to Olsson
                double F0Y = (1.0 / restLength) * ((N * elementDir.Y) + (MY0 * Z0.Y) - (MZ0 * Y0.Y) + (MY1 * Z1.Y) - (MZ1 * Y1.Y));
                double F0Z = (1.0 / restLength) * ((N * elementDir.Z) + (MY0 * Z0.Z) - (MZ0 * Y0.Z) + (MY1 * Z1.Z) - (MZ1 * Y1.Z)); 
                Vector3d F0 = new Vector3d(F0X, F0Y, F0Z);          //Unit: [N]

                axialMove = new Vector3d(F0);       //check

                //Force end
                Vector3d F1 = -1 * F0;
                //---------------------------------------------SOMETHING IS WRONG WITH AXIAL BEHAVIOUR (CHECK EXTENSION)-------------------------------------------//



                //Permutation symbol: Includes 6 non-zero components. Is a triple product of unit vectors (elementDir,y,z) of an orthogonal frame in a right-handed coordinate system
                //Moment start
                //i=1, j=2, k=3
                double M0X_pos = (-1.0) * (((MY0 * elementDir.Z * Z0.Y) / restLength) - ((MZ0 * elementDir.Z * Y0.Y) / restLength) + ((MX * ((Y0.Y * Z1.Z) - (Z0.Y * Y1.Z))) / 2.0));

                //i=2, j=3, k=1
                double M0Y_pos = (-1.0) * (((MY0 * elementDir.X * Z0.Z) / restLength) - ((MZ0 * elementDir.X * Y0.Z) / restLength) + ((MX * ((Y0.Z * Z1.X) - (Z0.Z * Y1.X))) / 2.0));

                //i=3, j=1, k=2
                double M0Z_pos = (-1.0) * (((MY0 * elementDir.Y * Z0.X) / restLength) - ((MZ0 * elementDir.Y * Y0.X) / restLength) + ((MX * ((Y0.X * Z1.Y) - (Z0.X * Y1.Y))) / 2.0));


                //i=1, j=3, k=2
                double M0X_neg = (1.0) * (((MY0 * elementDir.Y * Z0.Z) / restLength) - ((MZ0 * elementDir.Y * Y0.Z) / restLength) + ((MX * ((Y0.Z * Z1.Y) - (Z0.Z * Y1.Y))) / 2.0));

                //i=2, j=1, k=3
                double M0Y_neg = (1.0) * (((MY0 * elementDir.Z * Z0.X) / restLength) - ((MZ0 * elementDir.Z * Y0.X) / restLength) + ((MX * ((Y0.X * Z1.Z) - (Z0.X * Y1.Z))) / 2.0));

                //i=3, j=2, k=1
                double M0Z_neg = (1.0) * (((MY0 * elementDir.X * Z0.Y) / restLength) - ((MZ0 * elementDir.X * Y0.Y) / restLength) + ((MX * ((Y0.Y * Z1.X) - (Z0.Y * Y1.X))) / 2.0));

                //Sum of components
                Vector3d M0 = new Vector3d(M0X_pos + M0X_neg, M0Y_pos + M0Y_neg, M0Z_pos + M0Z_neg);          //Unit: [Nm]


                //Moment end
                //i=1, j=2, k=3
                double M1X_pos = (-1.0) * (((MY1 * elementDir.Z * Z1.Y) / restLength) - ((MZ1 * elementDir.Z * Y1.Y) / restLength) - ((MX * ((Y0.Y * Z1.Z) - (Z0.Y * Y1.Z))) / 2.0));       // (-) torsion term according to Olsson

                //i=2, j=3, k=1
                double M1Y_pos = (-1.0) * (((MY1 * elementDir.X * Z1.Z) / restLength) - ((MZ1 * elementDir.X * Y1.Z) / restLength) - ((MX * ((Y0.Z * Z1.X) - (Z0.Z * Y1.X))) / 2.0));

                //i=3, j=1, k=2
                double M1Z_pos = (-1.0) * (((MY1 * elementDir.Y * Z1.X) / restLength) - ((MZ1 * elementDir.Y * Y1.X) / restLength) - ((MX * ((Y0.X * Z1.Y) - (Z0.X * Y1.Y))) / 2.0));


                //i=1, j=3, k=2
                double M1X_neg = (1.0) * (((MY1 * elementDir.Y * Z1.Z) / restLength) - ((MZ1 * elementDir.Y * Y1.Z) / restLength) - ((MX * ((Y0.Z * Z1.Y) - (Z0.Z * Y1.Y))) / 2.0));

                //i=2, j=1, k=3
                double M1Y_neg = (1.0) * (((MY1 * elementDir.Z * Z1.X) / restLength) - ((MZ1 * elementDir.Z * Y1.X) / restLength) - ((MX * ((Y0.X * Z1.Z) - (Z0.X * Y1.Z))) / 2.0));

                //i=3, j=2, k=1
                double M1Z_neg = (1.0) * (((MY1 * elementDir.X * Z1.Y) / restLength) - ((MZ1 * elementDir.X * Y1.Y) / restLength) - ((MX * ((Y0.Y * Z1.X) - (Z0.Y * Y1.X))) / 2.0));

                //Sum of components
                Vector3d M1 = new Vector3d(M1X_pos + M1X_neg, M1Y_pos + M1Y_neg, M1Z_pos + M1Z_neg);            //Unit: [Nm]

                

                //Move and torque vectors
                Move[0] = F0;
                Move[1] = F1;

                Torque[0] = new Vector3d(0, 0, 0);
                Torque[1] = new Vector3d(0, 0, 0);
                //Torque[0] = M0;
                //Torque[1] = M1;
            }


            //Output moment in [kNm] and normal force/shear in [kN]
            public override object Output(List<KangarooSolver.Particle> p)
            {
                List<object> DataOut = new List<object>();

                //Updated local planes
                DataOut.Add(P0R);
                DataOut.Add(P1R);

                //Angles
                DataOut.Add(thetaX);
                DataOut.Add(thetaY0);
                DataOut.Add(thetaZ0);
                DataOut.Add(thetaY1);
                DataOut.Add(thetaZ1);

                //Other
                DataOut.Add(N);
                DataOut.Add(ext);
                DataOut.Add(extSimple);
                DataOut.Add(axialMove);


                //Element forces and moments
                //DataOut.Add(Math.Round(N * 1e-3, 3));           //Unit: [kN]
                //DataOut.Add(Math.Round(MY0 * 1e-3, 3));         //Unit: [kNm]
                //DataOut.Add(Math.Round(MZ0 * 1e-3, 3));         //Unit: [kNm]
                //DataOut.Add(Math.Round(MY1 * 1e-3, 3));         //Unit: [kNm]
                //DataOut.Add(Math.Round(MZ1 * 1e-3, 3));         //Unit: [kNm]
                //DataOut.Add(Math.Round(MX * 1e-3, 3));          //Unit: [kNm]


                return DataOut;



                //To do: create beam data object to store output information
                //DataTypes.BeamData beamData = new DataTypes.BeamData();
                //return beamData;
            }

        }



        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("f76cfabb-06ab-4869-a60d-c6c4865320ea"); }
        }
    }
}
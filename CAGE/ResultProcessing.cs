using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using System.Collections.Generic;

namespace CAGE
{
    public static class ResultProcessing
    {

        public static int ImageWidth = 1626;
        public static int ImageHeight = 1236;

        public static void runInOrder(string folder, string saveFolder)
        {
            (string UpperCameraImage, string LowerCameraImage)[] LabelPairs = pairUpImages(folder);

            List<(((double x, double y) head, (double x, double y) tail, double distance) uperFish, ((double x, double y) head, (double x, double y) tail, double distance) lowerFish)[]> AllFishPairs = new List<(((double x, double y) head, (double x, double y) tail, double distance) uperFish, ((double x, double y) head, (double x, double y) tail, double distance) lowerFish)[]>();

             foreach(var labelPair in LabelPairs)
            {
                string UpperLabels = File.ReadAllText(labelPair.UpperCameraImage);
                string LowerLabels = File.ReadAllText(labelPair.LowerCameraImage);

                List<(double x, double y)[]> UpperCoordinates = GetCoordinates(UpperLabels);
                List<(double x, double y)[]> LowerCoordinates = GetCoordinates(LowerLabels);

                UpperCoordinates = FindHeadAndTailCandidates(UpperCoordinates);
                LowerCoordinates = FindHeadAndTailCandidates(LowerCoordinates);

                UpperCoordinates = FilterHeadAndTailCandidates(UpperCoordinates);
                LowerCoordinates = FilterHeadAndTailCandidates(LowerCoordinates);

                List<((double x, double y) head, (double x, double y) tail, double distance)> UpperCoordinatesHeadTail = FindHeadAndTail(UpperCoordinates);
                List<((double x, double y) head, (double x, double y) tail, double distance)> LowerCoordinatesHeadTail = FindHeadAndTail(LowerCoordinates);

               (((double x, double y) head, (double x, double y) tail, double distance) uperFish, ((double x, double y) head, (double x, double y) tail, double distance) lowerFish)[] fishPairs = pairUpFish(UpperCoordinatesHeadTail, LowerCoordinatesHeadTail).ToArray();
                AllFishPairs.Add(fishPairs);
            };

            var fishFatPairs = AllFishPairs.SelectMany((afp) => afp);



        }
      
        //stero camera has 2 images Upper and lower
        //get file_path_pairs based on name
        //they have the same name the only difference is top camera is marked with t, bottom is marked with b
        public static (string UpperCameraImage, string LowerCamerImage)[] pairUpImages(string folder)
        {
            string[] files = Directory.GetFiles(folder,"*.txt");
            files = files.Select((file) => Path.GetFileNameWithoutExtension(file)).ToArray();

            string[] UpperCameraFiles = files.Where((e) => e.Contains("t")).ToArray();

            return UpperCameraFiles.Zip(UpperCameraFiles, (first, second) => (first, second.Replace("t", "b"))).ToArray();
        }


        //get (X,Y)[] coordinates for each fish detection
        public static List<(double x, double y)[]> GetCoordinates(string txt)
        {
            //go on x-axis and then find points where the x directions changes
            //need to be 4 fish 


            //each row in txt is a fish
            string[] fishes = txt.Split('\n');
            
            //get coordinates
            List<double[]> fishesCoordinates = fishes.Select((e)=> e.Split(" ", StringSplitOptions.RemoveEmptyEntries)
                                                                .Skip(1)
                                                                .Select((e)=> Double.Parse(e))
                                                                .ToArray()
                                                ).ToList();

            //convert to pixels
            fishesCoordinates = fishesCoordinates.Select((fish) =>
            {
                return fish.Select((coordinate, i) =>
                {
                    if (i % 2 == 0)
                        coordinate = coordinate * ImageWidth;
                    else
                        coordinate = coordinate * ImageHeight;

                    return coordinate;


                }).ToArray();


            }).ToList();

            //pair up (x,y)
            List<double[]> XCoordinates = fishesCoordinates.Select((fish)=> fish.Where((coordinate,i) => i % 2 == 0).ToArray()).ToList();
            List<double[]> YCoordinates = fishesCoordinates.Select((fish) => fish.Where((coordinate, i) => i % 2 != 0).ToArray()).ToList();

            List<(double x, double y)[]> fishCoordinatesZip = new List<(double x, double y)[]>();

            for(int i = 0; i < fishesCoordinates.Count; i++)
            {
                var zipped = XCoordinates[i].Zip(YCoordinates[i], (x, y) => (x, y)).ToArray();
                fishCoordinatesZip.Add(zipped);
            }

            return fishCoordinatesZip;
        }

        //find points where x is not going in the same direction.
        public static List<(double x, double y)[]> FindHeadAndTailCandidates(List<(double x, double y)[]> CoordinatesAllFish)
        {
            return CoordinatesAllFish.Select((Coordinates) =>
            {
                //direction left or right, left < 0
                //right > 0
                double x_last = Coordinates.First().x;
                double direction = 0;
                //returns all points where direction changes
                return Coordinates.Skip(1).Where((e) =>
                {
                    double newDirection = e.x- x_last;
                    x_last = e.x;
                    //checks if dirrection changed => both negative or both positive > 0
                    bool differentDirection = newDirection * direction < 0;
                    direction = newDirection;
                    return differentDirection;
                }).ToArray();
            }).ToList();         
        }

        //filter dots that are nearby
        //return if 4 dots remain (3 for tail, 1 for head)
        public static List<(double x, double y)[]> FilterHeadAndTailCandidates(List<(double x, double y)[]> Candidates, double epsilon = 5)
        {
            //get distance from each point (start with 0 index)
            //if distance 2 small remove all other points that are nearby.
            //repeat
            return Candidates.Where((candidate) =>{
                for (int i = 0; i < candidate.Length; i++)
                {

                    var comparatorCoordinates = candidate[i];
                    candidate = candidate.Skip(i).Where((e) => CalculateDistance(comparatorCoordinates.x, comparatorCoordinates.y, e.x, e.y) < epsilon).ToArray();
                }

                return candidate.Length == 4;
            }).ToList();
        }

        //get head tail and distance 
        public static List<((double x, double y) head, (double x, double y) tail, double distance)> FindHeadAndTail(List<(double x, double y)[]> Fishes){

            //find added distances for each dot, head is furthest away;
            return Fishes.Select((fish) =>
            {
                
                var distances = fish.Select((coordinates) =>
                {
                    return fish.Aggregate(0.0, (total, next) => total + CalculateDistance(coordinates.x, coordinates.y, next.x, next.y));
                }).ToArray();

                int HeadIndex = Array.IndexOf(distances,distances.Max());

                var head = fish[HeadIndex];

                //tail is always in the middle 
                var tail = fish.Where((e, i) => i != HeadIndex).OrderBy((e) => e.y).ToArray()[2];
                double distance = CalculateDistance(head.x, head.y, tail.x, tail.y);
                return (head, tail, distance);

            }).ToList();

           
         }

        public static double CalculateDistance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt(Math.Pow(x1 -x2,2) + Math.Pow(y1 -y2, 2));
        }
          

        //pair up fish on upper and lower camera
        public static List<(((double x, double y) head, (double x, double y) tail, double distance) uperFish, ((double x, double y) head, (double x, double y) tail, double distance) lowerFish)> pairUpFish(List<((double x, double y) head, (double x, double y) tail, double distance)> UperFishes, List<((double x, double y) head, (double x, double y) tail, double distance)>  LowerFishes, double epsilon = 5)
        {
            List<(((double x, double y) head, (double x, double y) tail, double distance) uperFish, ((double x, double y) head, (double x, double y) tail, double distance) lowerFish)> fishPairs = new List<(((double x, double y) head, (double x, double y) tail, double distance) uperFish, ((double x, double y) head, (double x, double y) tail, double distance) lowerFish)>();

            UperFishes.ForEach((upperFish) =>
            {
                //find first fish that is similar coordinates head, tail and distance
                var pairedFish = LowerFishes.FirstOrDefault((lowerFish) =>
                {
                    bool head = Math.Abs(lowerFish.head.x - upperFish.head.x) < epsilon && Math.Abs(lowerFish.head.y - upperFish.head.y) < epsilon;
                    bool tail = Math.Abs(lowerFish.tail.x - upperFish.tail.x) < epsilon && Math.Abs(lowerFish.tail.y - upperFish.tail.y) < epsilon;
                    bool distance = Math.Abs(lowerFish.distance - upperFish.distance) < epsilon;

                    return head && tail && distance;
                });

                LowerFishes.Remove(pairedFish);

                fishPairs.Add((upperFish, pairedFish));

            });

            return fishPairs;
        }

    }
}

from os import remove
import requests
import csv
import json
import transbigdata #坐标系转换

#2026/3/14
# 主要还是坐标系的转换,BD09有加密,解密出的WGS84位置不准,以单一的位置的url进行的爬取，无法保证路径绝对正确

# 2026/3/15
# 坐标系转换完成，但是主要还是json的解读问题,所以目前只能用手动输入geo的内容,但是好像就是半自动的...
#先搞了一个这个,再看看后面怎么处理吧


headers = {
       "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
}
def delete_splitkey(dict):
       dict_parts=dict.split("|");
       dict=dict_parts[2]
       dict=dict[2::]
       dict=dict[:-1]
       dict=dict.split(",")
       writein_csv(dict)

def writein_csv(dict):
       x=open("百度地图geo.csv", "w", encoding="UTF-8", newline="")
       map_data=csv.writer(x)
       map_data.writerow(["X","Y"])
       end=len(dict)
       total=end/2
       # 百度BD09MC加密坐标系
       for i in range(0,end,2):
              longitude=float(dict[i])
              latitude=float(dict[i+1])
              bd09_longitude,bd09_latitude=transbigdata.bd09mctobd09(longitude,latitude)
              gcj02_longitude,gcj02_latitude=transbigdata.bd09togcj02(bd09_longitude,bd09_latitude)
              final_longitude,final_latitude=transbigdata.gcj02towgs84(gcj02_longitude,gcj02_latitude)
              map_data.writerow([final_longitude,final_latitude]);
       x.close()
       print(f"数据已导出，共{total}个数据")



#?newmap
# url="https://ditu.amap.com/detail/get/detail?id=B0FFG0BPT6"
# 长沙火车站测试
# url="https://map.baidu.com/?uid=7658111c994128d78505bfc2&info_merge=1&isBizPoi=false&ugc_type=3&ugc_ver=1&qt=detailConInfo&device_ratio=2&compat=1&routetype=&sn_xy=&en_uid=7658111c994128d78505bfc2&t=1773480804951&auth=eByJdXN%40%40CF0U8wKIDcKwewdHGASzd5vuxNNBERVRVLtFLIYUOS29ID2Cxz6ZwWvvkGcuVtvvhguVtvyheuVtvCMGuVtvCQMuVtvIPcuxtw8wkv7uvZgMuVtv%40vcuVtvc3CuVtvcPPuVtveGvuRtDlp5C%40Bvh3CuVtvhgMuzVVtvrMhuxBto20N%3D5CGIbFUuuouKi3egvcguxNNBERVRVH&seckey=eWlrYT%2BydoVKGeksUVzhMMuFvzKfJueNGviQHrAECiA%3D%2CQrpNdUEceIakP3I8cC2topbCKNsjvgcwby8MoDxsZtEjEBrD-mYphq_aPUOLHf4wxUBvjT3i8El6RzINB-BJ-N4wuYJBdP-2rde4LYyNK5YBQKbSLsd38UXLXmLmqOj_WRxReyPQoeXe-qo31upv87Dpz6bRVnTMNqzi-I1F8LCo1X0NPVhfjPsGZ8V7lYEwJdlyLqexQiuqX_JmUgAC0B8jOlZwfqqC4Q_P448JzRNQ533v5ADfdu6wV-PuPpu1DeHYfcKxjljClXzTyf8kcuRPfBYinhAm7xTodI9RLR3i4nuWTMXWWkbOL8i383Ikh1XyQIdnXJcrmyN7sokLG-NQm3w6Mg9fC4CB7Y1K2MQ&pcevaname=pc4.1&newfrom=zhuzhan_webmap"
#长沙黄花机场测试
# url="https://map.baidu.com/?newmap=1&reqflag=pcmap&biz=1&from=webmap&da_par=direct&pcevaname=pc4.1&qt=s&da_src=shareurl&wd=%E9%95%BF%E6%B2%99%E9%BB%84%E8%8A%B1%E5%9B%BD%E9%99%85%E6%9C%BA%E5%9C%BA&c=158&src=0&wd2=%E9%95%BF%E6%B2%99%E5%B8%82%E9%95%BF%E6%B2%99%E5%8E%BF&pn=0&sug=1&l=13&b=(12569082,3235891;12620282,3262163)&from=webmap&biz_forward=%7B%22scaler%22:2,%22styles%22:%22pl%22%7D&sug_forward=c516fc89e549dd23f90cadcb&device_ratio=2&auth=4YJdI2Kfg8%40AcwLPyg8%409T3yOfDEFFHWuxNNBHENVNBtF%3DSYFSIF%3DOD2Cxz6ZwWvvkGcuVtvvhguVtvyheuVtvCMGuVtvCQMuVtvIPcuxtw8wkv7ucvY1SGpuxVtEgpT1aDv%40vcuVtvc3CuVtvcPPuVtveGvuBBEtHrvU2eGvh3CuVtvhgMuzVVtvrMhuxBRtTttnt3hJegvcguxNNBHENVNB&seckey=D%2FwzS5yH%2FyFk4nrnb7y6Ivd1dyYtv24TAThrxGrXkvw%3D%2C_VCldb01GZO9hbrkYLRcEIq-T32dHD2wZOifZFwxbPTkzwWJHOAONzuApO-jq5fTG2JNY-JiUAAaTNyg08_JEjCmjCDazClLhScvyR8GXdzBh_u82mR8jzc2axVL3AFB-qj2qM2E4VaPZaMBlMRiKn0CIqaYY14p41OJP_OGxtzCcoBOqvRO5h-w1ksnzhchrysA6EqRbF_xC2N48OZ7KmV61EREkETuAOX03k_1JJv0nGuAmicMWKd5i5bDIiKpP5hwIo-tfylKqRDACZyLS71Ud2TgXW1CIPb82BVrTnrssGtt5IXiwYy75IbhhF-B9BBasVYihYKwlnVBWrIKGg&tn=B_NORMAL_MAP&nn=0&u_loc=12579362,3246091&ie=utf-8&t=1773547074418&newfrom=zhuzhan_webmap"
#中南林业科技大学测试
# url="https://map.baidu.com/?newmap=1&reqflag=pcmap&biz=1&from=webmap&da_par=direct&pcevaname=pc4.1&qt=s&da_src=shareurl&wd=%E4%B8%AD%E5%8D%97%E6%9E%97%E4%B8%9A%E7%A7%91%E6%8A%80%E5%A4%A7%E5%AD%A6&c=257&src=0&wd2=%E9%95%BF%E6%B2%99%E5%B8%82%E5%A4%A9%E5%BF%83%E5%8C%BA&pn=0&sug=1&l=13&b=(12591157.694205036,2621929.381679137;12632115.542806404,2642945.877742714)&from=webmap&biz_forward=%7B%22scaler%22:2,%22styles%22:%22pl%22%7D&sug_forward=6a97ae603d582590364aae1c&device_ratio=2&auth=B9bKOxf5WJ%3D%40%40z9NCAgTD8KU%40zMgG9ALuxNNBHHVTNTtF%3DSYFSIF%3DODFCwyS8v7uvkGcuVtvvhguVtvyheuVtvCMGuVtcvY1SGpuNtCH7RB9AvIPcuVtvYvjuVtvZgMuVtv%40vcuVtvc3CuVtvcPPuVtveGvuVtveh3uVtvh3CuVtvhgMuxVVtvrMhuxt2dd9dv7uegvcguxNNBHHVTNT&seckey=D%2FwzS5yH%2FyFk4nrnb7y6Ivd1dyYtv24TAThrxGrXkvw%3D%2C_VCldb01GZO9hbrkYLRcEFDzznvs0vJMOjzwTfq8n8m-ii4AQAMupHfooZqIS2jJMSyfOLz3y9ls_pBXEm2J-Syr46YT6IsAtJoMsJnRfHB0MBBWVp5SYwdaalHNNcFdhUlT5__20g1w8uDOt1uIGYNAbsiXpVWNDVHV2OHJVFOUWC6Zg_GeGuQ1pcRE4sfg7wZ3wlymEFlOTJmGdZurmgxc40ofO6nnfESzMiUfMA7e2DA1zsGrDW_U2f_V0DuQRpZvf8zgO_iN7LeKtc1vMde3Lw2CtChMLu1HvRE6kAZ6YokZrzsmbmVYsM1XHi9rCkgD0pcWPS0ffQ-EGvYO5g&tn=B_NORMAL_MAP&nn=0&u_loc=12579362,3246091&ie=utf-8&t=1773550981280&newfrom=zhuzhan_webmap"



# a=requests.get(url,headers=headers)
# a=a.text
# data=json.loads(a)
span_area=input("请输入“geo”后的内容:\n")
delete_splitkey(span_area)




